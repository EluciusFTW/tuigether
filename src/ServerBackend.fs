module ServerBackend

open System
open System.Net
open System.Net.Http
open System.Net.Http.Json
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http.Connections.Client
open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.SignalR.Client
open Tuigether.Contracts

type Config = {
  Url: Uri
  ApiKey: string
  User: string
}

// ASP.NET Core reads and writes the contracts in camel case.
let private json = JsonSerializerOptions(JsonSerializerDefaults.Web)

let private headers (config: Config) = [
  RequestHeaders.ApiKey, config.ApiKey
  RequestHeaders.UserName, config.User
  RequestHeaders.ProtocolVersion, string Protocol.Version
]

let private retryDelay (previousAttempts: int64) =
  match previousAttempts with
  | 0L -> TimeSpan.Zero
  | 1L -> TimeSpan.FromSeconds 2.0
  | 2L -> TimeSpan.FromSeconds 5.0
  | _ -> TimeSpan.FromSeconds 10.0

let private unauthorizedReason = "The server rejects the API key. Check serverApiKey."

let rec private isUnauthorized (error: exn) =
  match error with
  | null -> false
  | :? HttpRequestException as e when e.StatusCode = Nullable HttpStatusCode.Unauthorized -> true
  | :? AggregateException as e -> e.InnerExceptions |> Seq.exists isUnauthorized
  | _ -> not (isNull error.InnerException) && isUnauthorized error.InnerException

// SignalR calls the listeners on its own threads.
type private Listeners<'a>() =
  let gate = obj ()
  let mutable listeners: ('a -> unit) list = []

  member _.Add(listener: 'a -> unit) : IDisposable =
    lock gate (fun () -> listeners <- listener :: listeners)

    { new IDisposable with
        member _.Dispose() =
          lock gate (fun () ->
            listeners <-
              listeners
              |> List.filter (fun other -> not (obj.ReferenceEquals(other, listener))))
    }

  member _.Send(value: 'a) =
    for listener in lock gate (fun () -> listeners) do
      listener value

// The one SignalR connection of the app. Each subscription registers its listeners on it.
type private Hub(config: Config, notify: string -> unit) =
  let gate = obj ()
  let mutable state = Backend.Connecting
  // The automatic reconnect reports a rejected API key only to the retry policy.
  let mutable reconnectUnauthorized = false
  let states = Listeners<Backend.ConnectionState>()
  let messages = Listeners<string * JsonElement>()
  let connects = Listeners<unit>()

  let connection =
    HubConnectionBuilder()
      .WithUrl(
        Uri(config.Url, HubProtocol.Path),
        fun (options: HttpConnectionOptions) ->
          for name, value in headers config do
            options.Headers[name] <- value
      )
      .WithAutomaticReconnect(
        { new IRetryPolicy with
            member _.NextRetryDelay context =
              match isUnauthorized context.RetryReason with
              | true ->
                reconnectUnauthorized <- true
                Nullable()
              | false -> Nullable(retryDelay context.PreviousRetryCount)
        }
      )
      .Build()

  let isRefused () =
    lock gate (fun () ->
      match state with
      | Backend.Refused _ -> true
      | _ -> false)

  // A refusal is final, so no later state replaces it.
  let setState (next: Backend.ConnectionState) =
    let changed =
      lock gate (fun () ->
        match state with
        | Backend.Refused _ -> false
        | current when current = next -> false
        | _ ->
          state <- next
          true)

    match changed with
    | true -> states.Send next
    | false -> ()

  let refuse (reason: string) =
    match isRefused () with
    | true -> ()
    | false ->
      setState (Backend.Refused reason)
      Log.line (sprintf "server refuses the connection: %s" reason)
      notify reason
      // SignalR deadlocks when a message handler waits for StopAsync.
      Task.Run(fun () -> connection.StopAsync()) |> ignore

  let connected () =
    setState Backend.Connected
    connects.Send()

  let receive (message: string) =
    let parsed =
      try
        use document = JsonDocument.Parse message
        let root = document.RootElement
        Some(root.GetProperty("type").GetString(), root.GetProperty("data").Clone())
      with error ->
        Log.line (sprintf "server message unreadable: %s" error.Message)
        None

    parsed |> Option.iter messages.Send

  // The automatic reconnect of SignalR does not cover the first connect.
  let rec connect (attempt: int64) =
    async {
      let! outcome =
        async {
          try
            do! connection.StartAsync() |> Async.AwaitTask
            return Ok()
          with error ->
            return Error error
        }

      match outcome, isRefused () with
      | _, true -> ()
      | Ok(), false -> connected ()
      | Error error, false when isUnauthorized error -> refuse unauthorizedReason
      | Error error, false ->
        match error.GetBaseException() with
        | :? HubException as rejection -> refuse rejection.Message
        | _ ->
          Log.line (sprintf "server connect failed: %s" error.Message)
          setState Backend.Disconnected
          do! Async.Sleep(int (retryDelay attempt).TotalMilliseconds)
          return! connect (attempt + 1L)
    }

  do
    connection.On<string>(HubProtocol.ReceiveMessage, Action<string> receive)
    |> ignore

    connection.On<ConnectRejected>(
      HubProtocol.ConnectRejected,
      Action<ConnectRejected>(fun rejected -> refuse rejected.Detail)
    )
    |> ignore

    connection.add_Reconnecting (fun _ ->
      setState Backend.Disconnected
      Task.CompletedTask)

    connection.add_Reconnected (fun _ ->
      connected ()
      Task.CompletedTask)

    // Closed fires when the server closes the connection without a reconnect, or when the refusal stops it.
    connection.add_Closed (fun _ ->
      match isRefused (), reconnectUnauthorized with
      | true, _ -> ()
      | false, true -> refuse unauthorizedReason
      | false, false ->
        setState Backend.Disconnected
        Async.Start(connect 1L)

      Task.CompletedTask)

  member _.Start() =
    Async.Start(connect 0L)

  member _.OnState(listener: Backend.ConnectionState -> unit) : IDisposable =
    let registration = states.Add listener
    listener (lock gate (fun () -> state))
    registration

  member _.OnMessage(listener: string * JsonElement -> unit) : IDisposable =
    messages.Add listener

  // Runs `action` now when the connection is up, and again after each later connect.
  member _.OnConnected(action: unit -> unit) : IDisposable =
    let registration = connects.Add action

    match lock gate (fun () -> state = Backend.Connected) with
    | true -> action ()
    | false -> ()

    registration

  // The hub reads a CloudEvents envelope, see `HubProtocol.ReceiveMessage`.
  member _.Send(message: obj) =
    let envelope =
      JsonSerializer.Serialize(
        {|
          ``type`` = message.GetType().FullName
          data = message
        |},
        json
      )

    async {
      try
        do!
          connection.InvokeAsync(HubProtocol.ReceiveMessage, envelope, CancellationToken.None)
          |> Async.AwaitTask
      with error ->
        Log.line (sprintf "server send of %s failed: %s" (message.GetType().Name) error.Message)
    }
    |> Async.Start

let private reasonOf (response: HttpResponseMessage) : Async<string> =
  async {
    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

    let problemText =
      try
        use document = JsonDocument.Parse body

        [ "detail"; "title" ]
        |> List.tryPick (fun name ->
          match document.RootElement.TryGetProperty name with
          | true, value when
            value.ValueKind = JsonValueKind.String
            && not (String.IsNullOrWhiteSpace(value.GetString()))
            ->
            Some(value.GetString())
          | _ -> None)
      with _ ->
        None

    return
      problemText
      |> Option.defaultWith (fun () ->
        sprintf "The server answers %d %s." (int response.StatusCode) response.ReasonPhrase)
  }

// The Client shows a rejection as a notification with the reason of the server.
let private post
  (http: HttpClient)
  (notify: string -> unit)
  (route: string)
  (command: 'command)
  (read: HttpResponseMessage -> Async<'result>)
  : Async<Result<'result, string>> =
  async {
    let! outcome =
      async {
        try
          use! response =
            http.PostAsJsonAsync(route, command, json, CancellationToken.None)
            |> Async.AwaitTask

          match response.IsSuccessStatusCode with
          | true ->
            let! result = read response
            return Ok result
          | false ->
            let! reason = reasonOf response
            return Error reason
        with error ->
          return Error(sprintf "%s failed: %s" typeof<'command>.Name error.Message)
      }

    match outcome with
    | Ok _ -> ()
    | Error reason ->
      Log.line (sprintf "server rejects %s: %s" typeof<'command>.Name reason)
      notify reason

    return outcome
  }

let private toStatus (status: SessionStatus) =
  match status with
  | SessionStatus.Started -> Session.Status.Started
  | SessionStatus.Finished -> Session.Status.Finished
  | _ -> Session.Status.Created

let private toData (summary: SessionSummary) : Session.Data = {
  Title = summary.Title
  Goal = ""
  StartedAt = summary.CreatedAt.ToUnixTimeMilliseconds()
  WorkStartedAt = 0L
  Creator = summary.CreatedBy
  ActiveDriver = null
  Status = Session.Status.toString (toStatus summary.Status)
  GoalLockOwner = null
  GoalLockedAt = 0L
  GitBranch = summary.Branch
  GitRepo = summary.Repository
  LastWipPushAt = 0L
  LastWipPushBy = null
}

let private sessionEvent (typeName: string, data: JsonElement) : Backend.SessionEvent option =
  match typeName with
  | name when name = typeof<SessionList>.FullName ->
    let list = data.Deserialize<SessionList> json

    let sessions =
      list.Sessions
      |> Seq.map (fun summary -> string summary.Id, toData summary)
      |> List.ofSeq

    Some(Backend.SessionsLoaded(sessions, Map.empty))
  | name when name = typeof<SessionSummary>.FullName ->
    let summary = data.Deserialize<SessionSummary> json
    Some(Backend.SessionChanged(string summary.Id, toData summary))
  | name when name = typeof<SessionRemoved>.FullName ->
    let removed = data.Deserialize<SessionRemoved> json
    Some(Backend.SessionRemoved(string removed.SessionId))
  | _ -> None

let private readSessionEvent (message: string * JsonElement) =
  try
    sessionEvent message
  with error ->
    Log.line (sprintf "server message %s unreadable: %s" (fst message) error.Message)
    None

let private disposeAll (registrations: IDisposable list) =
  { new IDisposable with
      member _.Dispose() =
        registrations |> List.iter _.Dispose()
  }

let private nothing = async { return () }

let create (config: Config) (notify: string -> unit) : Backend.Backend =
  let hub = Hub(config, notify)
  hub.Start()

  let http = new HttpClient(BaseAddress = config.Url)

  for name, value in headers config do
    http.DefaultRequestHeaders.Add(name, value)

  let post route command read =
    post http notify route command read

  {
    Connection =
      { new Backend.IConnection with
          member _.Subscription = [ [ "server-connection" ], (fun dispatch -> hub.OnState dispatch) ]
      }
    Sessions =
      { new Backend.ISessions with
          member _.Subscription = [
            [ "server-sessions" ],
            fun dispatch ->
              disposeAll [
                hub.OnMessage(readSessionEvent >> Option.iter dispatch)
                hub.OnConnected(fun () -> hub.Send(JoinSessionList()))
              ]
          ]

          // The server takes the user from the request headers.
          member _.Create _user title gitBranch gitRepo =
            post CommandRoutes.CreateSession (CreateSession(title, gitRepo, gitBranch)) (fun response ->
              async {
                let! created =
                  response.Content.ReadFromJsonAsync<CreateSessionResponse>(json, CancellationToken.None)
                  |> Async.AwaitTask

                return string created.SessionId
              })

          member _.Delete sessionId =
            match Guid.TryParse sessionId with
            | true, id -> post CommandRoutes.DeleteSession (DeleteSession id) (fun _ -> nothing)
            | false, _ -> async { return Error(sprintf "%s is not a Session id of the server." sessionId) }

          member _.Start _sessionId =
            nothing

          member _.DataSubscription _sessionId = []
      }
    Presence =
      { new Backend.IPresence with
          member _.Join _sessionId _user _avatarName =
            async { return Ok() }

          member _.Leave _sessionId _user _wasStarted _wasDriver =
            nothing

          member _.Subscription _sessionId _subscriberTag = []
      }
    Driving =
      { new Backend.IDriving with
          member _.Subscription _sessionId = []

          member _.RotateDriver _sessionId _by _currentDriver _endsCurrentDrive _nextDriver _proposed =
            nothing

          member _.PauseDrive _sessionId _by _driver _proposed =
            nothing

          member _.ResumeDrive _sessionId _by _driver _proposed =
            nothing

          member _.SkipDrive _sessionId _by _driver _proposed =
            nothing

          member _.EndDrive _sessionId _proposed =
            nothing

          member _.RecordFinishedDrive _sessionId _by _driver =
            nothing

          member _.EndBreak _sessionId _proposed =
            nothing

          member _.SkipBreak _sessionId _proposed =
            nothing

          member _.ClearDriver _sessionId =
            nothing
      }
    DriveHistory =
      { new Backend.IDriveHistory with
          member _.Subscription _sessionId = []
      }
    Notes =
      { new Backend.INotes with
          member _.Subscription _sessionId = []

          member _.AcquireEditLock _sessionId _lock =
            nothing

          member _.SaveDraft _sessionId _text _lock =
            nothing

          member _.EndEdit _sessionId _text =
            nothing
      }
    Goal =
      { new Backend.IGoal with
          member _.AcquireEditLock _sessionId _lock =
            nothing

          member _.SaveDraft _sessionId _text _lock =
            nothing

          member _.EndEdit _sessionId _text =
            nothing
      }
    Todo =
      { new Backend.ITodo with
          member _.Subscription _sessionId = []

          member _.Add _sessionId _itemId _text =
            nothing

          member _.ChangeText _sessionId _itemId _item =
            nothing

          member _.Complete _sessionId _itemId =
            nothing

          member _.Reopen _sessionId _itemId =
            nothing

          member _.Move _sessionId _itemId _newPosition ((_, _), (_, _)) =
            nothing

          member _.Remove _sessionId _itemId =
            nothing
      }
    NoteList =
      { new Backend.INoteList with
          member _.Subscription _sessionId = []

          member _.Add _sessionId _itemId _text =
            nothing

          member _.Change _sessionId _itemId _text =
            nothing

          member _.Remove _sessionId _itemId =
            nothing
      }
    Git =
      { new Backend.IGit with
          member _.ChangeBranch _sessionId _branch =
            nothing

          member _.RecordWipPush _sessionId _user _pushedAt =
            nothing
      }
  }
