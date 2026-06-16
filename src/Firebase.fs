module Firebase

open System
open System.Threading.Tasks
open Firebase.Database
open Firebase.Database.Query
open Firebase.Database.Streaming

type Config = { Url: string; Secret: string }

// A Firebase client paired with the session it's scoped to. Held by every per-session
// component (Timer, Notes, TodoList, DriveLog, Journey) so they can issue writes/reads.
type Persistence = { Client: FirebaseClient; SessionId: string }

// Events emitted by the global sessions-list stream (used by SessionList).
type SessionEvent =
  | SessionsLoaded of (string * Session.Data) list
  | SessionChanged of string * Session.Data
  | SessionRemoved of string
  | ConnectionError of string

// Events emitted by the per-session connectedUsers stream (used by Avatar).
type UserEvent =
  | UserChanged of user: string * presence: Session.UserPresence
  | UserRemoved of user: string

let private sessionsPath = "sessions"

let createClient (cfg: Config) =
  let options = FirebaseOptions(AuthTokenAsyncFactory = Func<Task<string>>(fun () -> Task.FromResult cfg.Secret))

  new FirebaseClient(cfg.Url, options)

let private formatError (e: exn) =
  sprintf "[%s] %s" (e.GetType().Name) e.Message

// All session widget writes are best-effort fire-and-forget: a dropped write is
// re-synced on the next change event, so failures are swallowed rather than
// surfaced. These helpers centralize that pattern (previously a try/with per write).
let private swallow (work: Async<unit>) : Async<unit> =
  async {
    try
      do! work
    with _ ->
      ()
  }

let private setValue (node: ChildQuery) (value: 'a) : Async<unit> =
  swallow (node.PutAsync(box value) |> Async.AwaitTask)

let private removeValue (node: ChildQuery) : Async<unit> =
  swallow (node.DeleteAsync() |> Async.AwaitTask)

// ─── Generic reload-on-change subscription ───────────────────────────────────
//
// A path's children may be heterogeneous (scalars + dictionaries), so
// AsObservable<T> cannot deserialize per-child events into a full T. Treat
// any child event as a "something changed" signal and re-load the full
// payload via the supplied `load` callback. An explicit initial load runs
// once on registration so callers see current state without waiting for a
// remote change.
let private subscribeReload<'T>
  (pathQuery: ChildQuery)
  (load: unit -> Async<'T option>)
  (dispatch: 'T option -> unit)
  (onErrorMsg: string -> unit)
  : IDisposable =
  // Coalesce reload requests. The Firebase observable fires once per existing
  // child on subscribe, so naive Async.Start per event would dispatch the same
  // state N times in parallel. Keep one load in flight; if requests arrive
  // while loading, run exactly one follow-up after it.
  let gate = obj ()
  let mutable inFlight = false
  let mutable pending = false

  let rec runLoad () =
    async {
      try
        let! state = load ()
        dispatch state
      with e ->
        onErrorMsg (formatError e)

      let runAgain =
        lock gate (fun () ->
          match pending with
          | true ->
            pending <- false
            true
          | false ->
            inFlight <- false
            false)

      match runAgain with
      | true -> return! runLoad ()
      | false -> ()
    }

  let triggerReload () =
    let shouldStart =
      lock gate (fun () ->
        match inFlight with
        | true ->
          pending <- true
          false
        | false ->
          inFlight <- true
          true)

    match shouldStart with
    | true -> Async.Start(runLoad ())
    | false -> ()

  triggerReload ()

  let onNext (_ev: FirebaseEvent<obj>) =
    triggerReload ()

  let onError (e: exn) =
    onErrorMsg (formatError e)

  pathQuery.AsObservable<obj>().Subscribe(Action<FirebaseEvent<obj>> onNext, Action<exn> onError)

// ─── Sessions stream ─────────────────────────────────────────────────────────

module Sessions =

  let private sessionNode (client: FirebaseClient) (sessionId: string) =
    client.Child(sessionsPath).Child(sessionId)

  // Load the full sessions list. Like Session.loadData, this re-reads the whole
  // payload so the reload-on-change subscription always sees a consistent snapshot.
  let loadAll (client: FirebaseClient) : Async<(string * Session.Data) list option> =
    async {
      try
        let! sessions = client.Child(sessionsPath).OnceAsync<Session.Data>() |> Async.AwaitTask

        return
          sessions
          |> Seq.map (fun o -> o.Key, o.Object)
          |> Seq.toList
          |> Some
      with _ ->
        return None
    }

  // Mirror the session-details sync: any change anywhere under /sessions (a new
  // session, a deleted one, or a nested widget edit) triggers a full reload via
  // subscribeReload, which also performs an initial load on registration. This is
  // far more reliable than deserializing heterogeneous per-child streaming events
  // into Session.Data, which silently dropped new-session events.
  let subscription (client: FirebaseClient) (wrap: SessionEvent -> 'appMsg) _ = [
    [ "firebase-sessions" ],
    fun dispatch ->
      subscribeReload
        (client.Child(sessionsPath))
        (fun () -> loadAll client)
        (function
          | Some sessions -> dispatch (wrap (SessionsLoaded sessions))
          | None -> ())
        (fun e -> dispatch (wrap (ConnectionError e)))
  ]

  let create
    (client: FirebaseClient)
    (user: string)
    (title: string)
    (gitBranch: string)
    (gitRepo: string)
    : Async<Result<string, string>> =
    async {
      try
        let data = {
          Session.Data.Title = title
          Session.Data.Goal = ""
          Session.Data.StartedAt = Clock.nowMs ()
          Session.Data.WorkStartedAt = 0L
          Session.Data.Creator = user
          Session.Data.ActiveDriver = null
          Session.Data.Status = Session.Status.toString Session.Status.Created
          Session.Data.GoalLockOwner = null
          Session.Data.GoalLockedAt = 0L
          Session.Data.GitBranch = gitBranch
          Session.Data.GitRepo = gitRepo
          Session.Data.LastWipPushAt = 0L
          Session.Data.LastWipPushBy = null
        }

        let! result = client.Child(sessionsPath).PostAsync(data) |> Async.AwaitTask
        return Ok result.Key
      with e ->
        return Error e.Message
    }

  let setStatus (client: FirebaseClient) (sessionId: string) (status: Session.Status) : Async<unit> =
    async {
      let node = sessionNode client sessionId
      do! setValue (node.Child "Status") (Session.Status.toString status)

      match status with
      | Session.Status.Started -> do! setValue (node.Child "WorkStartedAt") (Clock.nowMs ())
      | _ -> ()
    }

  let delete (client: FirebaseClient) (sessionId: string) : Async<Result<unit, string>> =
    async {
      try
        do! (sessionNode client sessionId).DeleteAsync() |> Async.AwaitTask
        return Ok()
      with e ->
        return Error e.Message
    }

  let setActiveDriver (client: FirebaseClient) (sessionId: string) (user: string option) : Async<unit> =
    let node = (sessionNode client sessionId).Child "ActiveDriver"

    match user with
    | Some u -> setValue node u
    | None -> removeValue node

  let saveWipPush (client: FirebaseClient) (sessionId: string) (user: string) (timestamp: int64) : Async<unit> =
    async {
      let node = sessionNode client sessionId
      do! setValue (node.Child "LastWipPushBy") user
      do! setValue (node.Child "LastWipPushAt") timestamp
    }

  let saveGitBranch (client: FirebaseClient) (sessionId: string) (branch: string) : Async<unit> =
    setValue ((sessionNode client sessionId).Child "GitBranch") branch

  let saveGoal (client: FirebaseClient) (sessionId: string) (text: string) : Async<unit> =
    setValue ((sessionNode client sessionId).Child "Goal") text

  let saveGoalLock (client: FirebaseClient) (sessionId: string) (owner: string) (lockedAt: int64) : Async<unit> =
    async {
      let node = sessionNode client sessionId
      do! setValue (node.Child "GoalLockOwner") owner
      do! setValue (node.Child "GoalLockedAt") lockedAt
    }

  let releaseGoalLock (client: FirebaseClient) (sessionId: string) : Async<unit> =
    async {
      let node = sessionNode client sessionId
      do! removeValue (node.Child "GoalLockOwner")
      do! removeValue (node.Child "GoalLockedAt")
    }

  let loadData (client: FirebaseClient) (sessionId: string) : Async<Session.Data option> =
    async {
      try
        let! result =
          (sessionNode client sessionId).OnceSingleAsync<Session.Data>()
          |> Async.AwaitTask

        return
          match isNull (box result) with
          | true -> None
          | false -> Some result
      with _ ->
        return None
    }

  let dataSubscription (client: FirebaseClient) (sessionId: string) (wrap: Session.Data option -> 'appMsg) = [
    [ "session-data"; sessionId ],
    fun dispatch ->
      subscribeReload
        (sessionNode client sessionId)
        (fun () -> loadData client sessionId)
        (wrap >> dispatch)
        (fun _ -> ())
  ]

// ─── Connected users (Avatar) ────────────────────────────────────────────────

module Users =

  let private connectedUsersPath (client: FirebaseClient) (sessionId: string) =
    client.Child(sessionsPath).Child(sessionId).Child("widgetState").Child("connectedUsers")

  let private subscribeConnectedUsers
    (client: FirebaseClient)
    (sessionId: string)
    (dispatch: UserEvent -> unit)
    (onConnectionError: string -> unit)
    : IDisposable =
    let onNext (ev: FirebaseEvent<Session.UserPresence>) =
      try
        match String.IsNullOrEmpty ev.Key with
        | true -> ()
        | false ->
          match ev.EventType with
          | FirebaseEventType.Delete -> dispatch (UserRemoved ev.Key)
          | _ ->
            match isNull (box ev.Object) with
            | true -> ()
            | false -> dispatch (UserChanged(ev.Key, ev.Object))
      with e ->
        onConnectionError (formatError e)

    let onError (e: exn) =
      onConnectionError (formatError e)

    (connectedUsersPath client sessionId)
      .AsObservable<Session.UserPresence>()
      .Subscribe(Action<FirebaseEvent<Session.UserPresence>> onNext, Action<exn> onError)

  // `subscriberTag` distinguishes subscribers of the same session's connectedUsers
  // path. Elmish keys subscriptions by their key list and keeps the first-registered
  // start function for a given key, so two consumers (e.g. SessionList and Journey)
  // sharing one key would silently route all events to whichever registered first.
  let subscription (client: FirebaseClient) (sessionId: string) (subscriberTag: string) (wrap: UserEvent -> 'appMsg) = [
    [ "connected-users"; subscriberTag; sessionId ],
    fun dispatch -> subscribeConnectedUsers client sessionId (wrap >> dispatch) (fun _ -> ())
  ]

  let join
    (client: FirebaseClient)
    (sessionId: string)
    (user: string)
    (avatarName: string)
    : Async<Result<unit, string>> =
    async {
      try
        let presence = {
          Session.UserPresence.Avatar = avatarName
          Session.UserPresence.Mood = "Neutral"
        }

        do!
          (connectedUsersPath client sessionId).Child(user).PutAsync(presence :> obj)
          |> Async.AwaitTask

        return Ok()
      with e ->
        return Error e.Message
    }

  let leave (client: FirebaseClient) (sessionId: string) (user: string) : Async<Result<unit, string>> =
    async {
      try
        do!
          (connectedUsersPath client sessionId).Child(user).DeleteAsync()
          |> Async.AwaitTask

        return Ok()
      with e ->
        return Error e.Message
    }

  // Performs leave, then reads remaining connected users. The boolean indicates
  // whether the session is now empty — used by callers to drive the
  // Started → Finished transition without an extra roundtrip.
  let leaveAndCheckLast (client: FirebaseClient) (sessionId: string) (user: string) : Async<Result<bool, string>> =
    async {
      try
        do!
          (connectedUsersPath client sessionId).Child(user).DeleteAsync()
          |> Async.AwaitTask

        let! remaining =
          (connectedUsersPath client sessionId).OnceAsync<Session.UserPresence>()
          |> Async.AwaitTask

        return Ok(Seq.isEmpty remaining)
      with e ->
        return Error e.Message
    }

  let setPresence
    (client: FirebaseClient)
    (sessionId: string)
    (user: string)
    (avatarName: string)
    (moodName: string)
    : Async<unit> =
    let presence = {
      Session.UserPresence.Avatar = avatarName
      Session.UserPresence.Mood = moodName
    }

    setValue ((connectedUsersPath client sessionId).Child user) presence

// ─── Push IDs ────────────────────────────────────────────────────────────────
//
// Port of Firebase's client-side push-ID algorithm: 20-character lexicographically
// sortable IDs (8 chars timestamp + 12 chars randomness). Concurrent generation
// across users yields distinct keys, so per-item writes never collide.

module PushId =

  let private chars = "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz"

  let private rng = Random()
  let private syncLock = obj ()
  let private lastRandChars: int array = Array.zeroCreate 12
  let mutable private lastPushTime = 0L

  let generate () =
    lock syncLock (fun () ->
      let timestamp = Clock.nowMs ()
      let duplicateTime = timestamp = lastPushTime
      lastPushTime <- timestamp

      let timestampChars = Array.zeroCreate<char> 8
      let mutable remaining = timestamp

      for i in 7..-1..0 do
        timestampChars.[i] <- chars.[int (remaining % 64L)]
        remaining <- remaining / 64L

      match duplicateTime with
      | false ->
        for i in 0..11 do
          lastRandChars.[i] <- rng.Next(64)
      | true ->
        let mutable carryIndex = 11

        while carryIndex >= 0 && lastRandChars.[carryIndex] = 63 do
          lastRandChars.[carryIndex] <- 0
          carryIndex <- carryIndex - 1

        lastRandChars.[carryIndex] <- lastRandChars.[carryIndex] + 1

      let randChars = lastRandChars |> Array.map (fun n -> chars.[n])
      String(timestampChars) + String(randChars))

// ─── Notes ───────────────────────────────────────────────────────────────────

module Notes =

  let private notesPath (client: FirebaseClient) (sessionId: string) =
    client.Child(sessionsPath).Child(sessionId).Child("widgetState").Child("notes")

  let saveFreetext (client: FirebaseClient) (sessionId: string) (text: string) : Async<unit> =
    setValue ((notesPath client sessionId).Child "FreetextContent") text

  let saveLock (client: FirebaseClient) (sessionId: string) (owner: string) (lockedAt: int64) : Async<unit> =
    async {
      let node = notesPath client sessionId
      do! setValue (node.Child "LockOwner") owner
      do! setValue (node.Child "LockedAt") lockedAt
    }

  let releaseLock (client: FirebaseClient) (sessionId: string) : Async<unit> =
    async {
      let node = notesPath client sessionId
      do! removeValue (node.Child "LockOwner")
      do! removeValue (node.Child "LockedAt")
    }

  let private loadField<'T> (client: FirebaseClient) (sessionId: string) (key: string) : Async<'T> =
    async {
      try
        let! result = (notesPath client sessionId).Child(key).OnceSingleAsync<'T>() |> Async.AwaitTask
        return result
      with _ ->
        return Unchecked.defaultof<'T>
    }

  // Load each field independently so a corrupted field does not poison the
  // others — each just degrades to its default.
  let load (client: FirebaseClient) (sessionId: string) : Async<Session.NotesState option> =
    async {
      try
        let! freetext = loadField<string> client sessionId "FreetextContent"
        let! lockOwner = loadField<string> client sessionId "LockOwner"
        let! lockedAt = loadField<int64> client sessionId "LockedAt"

        return
          Some {
            FreetextContent = freetext
            LockOwner = lockOwner
            LockedAt = lockedAt
          }
      with _ ->
        return None
    }

  let subscription (client: FirebaseClient) (sessionId: string) (wrap: Session.NotesState option -> 'appMsg) = [
    [ "notes-state"; sessionId ],
    fun dispatch ->
      subscribeReload (notesPath client sessionId) (fun () -> load client sessionId) (wrap >> dispatch) (fun _ -> ())
  ]

// ─── List ────────────────────────────────────────────────────────────────────

module NoteList =

  let private listPath (client: FirebaseClient) (sessionId: string) =
    client.Child(sessionsPath).Child(sessionId).Child("widgetState").Child("list")

  let addItem (client: FirebaseClient) (sessionId: string) (itemId: string) (text: string) : Async<unit> =
    setValue ((listPath client sessionId).Child("Items").Child itemId) text

  let deleteItem (client: FirebaseClient) (sessionId: string) (itemId: string) : Async<unit> =
    removeValue ((listPath client sessionId).Child("Items").Child itemId)

  // A corrupted Items shape (e.g. legacy sessions where Firebase coerced
  // integer-keyed dicts into JSON arrays) degrades to an empty list.
  let load (client: FirebaseClient) (sessionId: string) : Async<Session.ListState option> =
    async {
      try
        let! items =
          async {
            try
              let! result =
                (listPath client sessionId).Child("Items").OnceSingleAsync<System.Collections.Generic.Dictionary<string, string>>()
                |> Async.AwaitTask

              return result
            with _ ->
              return Unchecked.defaultof<System.Collections.Generic.Dictionary<string, string>>
          }

        return Some { Items = items }
      with _ ->
        return None
    }

  let subscription (client: FirebaseClient) (sessionId: string) (wrap: Session.ListState option -> 'appMsg) = [
    [ "list-state"; sessionId ],
    fun dispatch ->
      subscribeReload (listPath client sessionId) (fun () -> load client sessionId) (wrap >> dispatch) (fun _ -> ())
  ]

// ─── Todo ────────────────────────────────────────────────────────────────────

module Todo =

  let private todoPath (client: FirebaseClient) (sessionId: string) =
    client.Child(sessionsPath).Child(sessionId).Child("widgetState").Child("todo")

  let private itemNode (client: FirebaseClient) (sessionId: string) (itemId: string) =
    (todoPath client sessionId).Child("Items").Child itemId

  let addItem (client: FirebaseClient) (sessionId: string) (itemId: string) (text: string) : Async<unit> =
    async {
      let node = itemNode client sessionId itemId
      do! setValue (node.Child "Text") text
      do! setValue (node.Child "Completed") false
    }

  let setItem
    (client: FirebaseClient)
    (sessionId: string)
    (itemId: string)
    (text: string)
    (completed: bool)
    : Async<unit> =
    async {
      let node = itemNode client sessionId itemId
      do! setValue (node.Child "Text") text
      do! setValue (node.Child "Completed") completed
    }

  let setCompleted (client: FirebaseClient) (sessionId: string) (itemId: string) (completed: bool) : Async<unit> =
    setValue ((itemNode client sessionId itemId).Child "Completed") completed

  let deleteItem (client: FirebaseClient) (sessionId: string) (itemId: string) : Async<unit> =
    removeValue (itemNode client sessionId itemId)

  let load (client: FirebaseClient) (sessionId: string) : Async<Session.TodoState option> =
    async {
      try
        let! items =
          (todoPath client sessionId)
            .Child("Items")
            .OnceSingleAsync<System.Collections.Generic.Dictionary<string, Session.TodoItemState>>()
          |> Async.AwaitTask

        return Some { Items = items }
      with _ ->
        return None
    }

  let subscription (client: FirebaseClient) (sessionId: string) (wrap: Session.TodoState option -> 'appMsg) = [
    [ "todo-state"; sessionId ],
    fun dispatch ->
      subscribeReload (todoPath client sessionId) (fun () -> load client sessionId) (wrap >> dispatch) (fun _ -> ())
  ]

// ─── Timer ───────────────────────────────────────────────────────────────────

module Timer =

  let private timerPath (client: FirebaseClient) (sessionId: string) =
    client.Child(sessionsPath).Child(sessionId).Child("widgetState").Child("timer")

  let save (client: FirebaseClient) (sessionId: string) (state: Session.TimerState) : Async<unit> =
    setValue (timerPath client sessionId) state

  let load (client: FirebaseClient) (sessionId: string) : Async<Session.TimerState option> =
    async {
      try
        let! result =
          (timerPath client sessionId).OnceSingleAsync<Session.TimerState>()
          |> Async.AwaitTask

        return
          match isNull (box result) with
          | true -> None
          | false -> Some result
      with _ ->
        return None
    }

  let subscription (client: FirebaseClient) (sessionId: string) (wrap: Session.TimerState option -> 'appMsg) = [
    [ "timer-state"; sessionId ],
    fun dispatch ->
      subscribeReload (timerPath client sessionId) (fun () -> load client sessionId) (wrap >> dispatch) (fun _ -> ())
  ]

  // Pause a running drive remotely — used when the active driver leaves/quits so the
  // drive doesn't keep counting down with nobody at the wheel. Freezes the remaining
  // time (derived from the authoritative EndsAt) and clears EndsAt. Returns whether it
  // actually paused (i.e. a drive was running), so the caller can log a Paused event.
  let pauseIfRunning (client: FirebaseClient) (sessionId: string) : Async<bool> =
    async {
      try
        let! state = load client sessionId

        match state with
        | Some s when s.IsRunning ->
          let now = Clock.nowMs ()
          let remainingSeconds = max 0 (int ((s.EndsAt - now) / 1000L))

          do!
            save client sessionId {
              Session.TimerState.RemainingSeconds = remainingSeconds
              Session.TimerState.IsRunning = false
              Session.TimerState.EndsAt = 0L
            }

          return true
        | _ -> return false
      with _ ->
        return false
    }

// ─── Drive history ───────────────────────────────────────────────────────────

module History =

  let private historyPath (client: FirebaseClient) (sessionId: string) =
    client.Child(sessionsPath).Child(sessionId).Child("driveHistory")

  // Append-only: PostAsync mints a chronological push key per event, so the log
  // preserves order and never overwrites prior entries.
  let append (client: FirebaseClient) (sessionId: string) (event: Session.DriveEvent) : Async<unit> =
    swallow ((historyPath client sessionId).PostAsync(box event) |> Async.AwaitTask |> Async.Ignore)

  // The whole log is a dictionary of pushKey -> event; push keys sort chronologically.
  let load
    (client: FirebaseClient)
    (sessionId: string)
    : Async<System.Collections.Generic.Dictionary<string, Session.DriveEvent> option> =
    async {
      try
        let! events =
          (historyPath client sessionId)
            .OnceSingleAsync<System.Collections.Generic.Dictionary<string, Session.DriveEvent>>()
          |> Async.AwaitTask

        return
          match isNull (box events) with
          | true -> None
          | false -> Some events
      with _ ->
        return None
    }

  let subscription
    (client: FirebaseClient)
    (sessionId: string)
    (wrap: System.Collections.Generic.Dictionary<string, Session.DriveEvent> option -> 'appMsg)
    =
    [
      [ "drive-history"; sessionId ],
      fun dispatch ->
        subscribeReload (historyPath client sessionId) (fun () -> load client sessionId) (wrap >> dispatch) (fun _ -> ())
    ]
