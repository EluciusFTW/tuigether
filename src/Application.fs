module Application

open System
open Dependencies
open Elmish
open Firebase.Database
open Spectre.Tui
open Spectre.Tui.App
open SpectreTuff.Layout
open SpectreTuff.Widgets

type Page =
  | SessionListPage
  | SessionViewPage of SessionView.Model

type Model = {
  Page: Page
  SessionList: SessionList.Model
  User: string
  AvatarName: string
  Focus: int
  LogVisible: bool
  LogModel: Log.Model
  Exiting: bool
}

type Msg =
  | InputMsg of Input.Msg
  | SessionListMsg of SessionList.Msg
  | SessionViewMsg of SessionView.Msg
  | LeaveFinalized
  | ToggleLog
  | Tick
  | Exit

type Panel = {
  Number: int
  Title: string
  LayoutSlot: AppLayout.Slot
  Focused: bool
  Boxed: bool
  CapturesInput: bool
  Widget: IWidget
  KeyMap: IKeyMap
  HandleKey: ConsoleKeyInfo -> Msg option
  Update: Msg -> Model -> (Model * Cmd<Msg>) option
}

let exitEvent = new Threading.ManualResetEventSlim false

// The global keys' behavior and their help-bar documentation are derived from a single
// binding list, so a key and its description can never drift apart.
let private globalBindings: Keymap.KeyBinding<unit, Msg> list = [
  Keymap.KeyBinding.create 'q' "quit" Exit
  Keymap.KeyBinding.create 'l' "toggle log" ToggleLog
]

let private handleGlobalKey (key: ConsoleKeyInfo) : Msg option =
  Keymap.KeyBinding.handleKey globalBindings key ()

let private globalKeyMap: IKeyMap =
  Keymap.KeyBinding.toKeyMap globalBindings ()

let private buildPanels (model: Model) : Panel list =
  match model.Page with
  | SessionListPage -> [
      {
        Number = 1
        Title = "Sessions"
        LayoutSlot = AppLayout.Content
        Focused = model.Focus = 1
        Boxed = true
        CapturesInput = SessionList.capturesInput model.SessionList
        Widget = SessionList.widget model.SessionList
        KeyMap = SessionList.keyMap model.SessionList
        HandleKey = fun key -> SessionList.handleKey key model.SessionList |> Option.map SessionListMsg
        Update = fun _ _ -> None
      }
    ]
  | SessionViewPage viewModel -> [
      {
        Number = 1
        Title = "Session"
        LayoutSlot = AppLayout.Content
        Focused = model.Focus = 1
        Boxed = false
        CapturesInput = SessionView.capturesInput viewModel
        Widget = SessionView.widget viewModel
        KeyMap = SessionView.keyMap viewModel
        HandleKey = fun key -> SessionView.handleKey key viewModel |> Option.map SessionViewMsg
        Update = fun _ _ -> None
      }
    ]

let init (client: FirebaseClient) (user: string) () =
  let listModel, listCmd = SessionList.init client user ()

  let avatarName = Journey.resolveName ()

  {
    Page = SessionListPage
    SessionList = listModel
    User = user
    AvatarName = avatarName
    Focus = 1
    LogVisible = false
    LogModel = Log.init ()
    Exiting = false
  },
  Cmd.map SessionListMsg listCmd

let private handleSessionListOutMsg
  (client: FirebaseClient)
  (user: string)
  (avatarName: string)
  (model: Model)
  (out: SessionList.OutMsg option)
  : Model * Cmd<Msg> =
  match out with
  | Some(SessionList.OpenSession(sessionId, sessionData)) ->
    let viewModel, viewCmd = SessionView.init client user avatarName sessionId sessionData

    {
      model with
          Page = SessionViewPage viewModel
    },
    Cmd.map SessionViewMsg viewCmd
  | None -> model, []

let private leaveFinalizeCmd
  (client: FirebaseClient)
  (sessionId: string)
  (user: string)
  (wasStarted: bool)
  (wasDriver: bool)
  : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () ->
      async {
        // If the active driver is leaving, pause the running drive first so it stops
        // counting down with nobody driving. Awaited here (not dispatched as a message)
        // so it completes even on quit, before the app exits via LeaveFinalized.
        match wasDriver with
        | true ->
          let! paused = Firebase.Timer.pauseIfRunning client sessionId

          match paused with
          | true ->
            do!
              Firebase.History.append client sessionId {
                Session.DriveEvent.Type = Session.DriveEventType.toString Session.DriveEventType.Paused
                Session.DriveEvent.Driver = user
                Session.DriveEvent.By = user
                Session.DriveEvent.At = Clock.nowMs ()
              }
          | false -> ()
        | false -> ()

        let! result = Firebase.Users.leaveAndCheckLast client sessionId user

        match result, wasStarted with
        | Ok true, true -> do! Firebase.Sessions.setStatus client sessionId Session.Status.Finished
        | _ -> ()
      })
    ()
    (fun () -> LeaveFinalized)

let private handleSessionViewOutMsg
  (client: FirebaseClient)
  (model: Model)
  (out: SessionView.OutMsg option)
  : Model * Cmd<Msg> =
  match out with
  | Some(SessionView.LeaveSession(sessionId, user, wasStarted, wasDriver)) ->
    { model with Page = SessionListPage }, leaveFinalizeCmd client sessionId user wasStarted wasDriver
  | None -> model, []

let update (deps: Dependencies) (user: string) msg model =
  match msg with
  | InputMsg(Input.KeyPressed key) ->
    let panels = buildPanels model
    let focusedPanel = panels |> List.tryFind (fun p -> p.Number = model.Focus)
    let capturing = focusedPanel |> Option.exists (_.CapturesInput)

    match capturing with
    | true ->
      focusedPanel
      |> Option.bind (fun p -> p.HandleKey key)
      |> Option.map (fun msg -> model, Cmd.ofMsg msg)
      |> Option.defaultValue (model, [])
    | false ->
      match handleGlobalKey key with
      | Some msg -> model, Cmd.ofMsg msg
      | None ->
        focusedPanel
        |> Option.bind (fun p -> p.HandleKey key)
        |> Option.map (fun msg -> model, Cmd.ofMsg msg)
        |> Option.defaultValue (model, [])

  | SessionListMsg lMsg ->
    let listModel, listCmd, outMsg = SessionList.update lMsg model.SessionList

    let modelAfterList = { model with SessionList = listModel }
    let modelAfterOut, outCmd = handleSessionListOutMsg deps.Client user model.AvatarName modelAfterList outMsg
    modelAfterOut, Cmd.batch [ Cmd.map SessionListMsg listCmd; outCmd ]

  | SessionViewMsg vMsg ->
    match model.Page with
    | SessionViewPage viewModel ->
      let m, sessionCmd, outMsg = SessionView.update deps vMsg viewModel
      let modelAfterView = { model with Page = SessionViewPage m }
      let modelAfterOut, outCmd = handleSessionViewOutMsg deps.Client modelAfterView outMsg
      modelAfterOut, Cmd.batch [ Cmd.map SessionViewMsg sessionCmd; outCmd ]
    | _ -> model, []

  | LeaveFinalized ->
    match model.Exiting with
    | true ->
      exitEvent.Set()
      model, []
    | false -> model, []

  | ToggleLog ->
    {
      model with
          LogVisible = not model.LogVisible
    },
    []

  | Tick -> model, []

  | Exit ->
    match model.Page, model.Exiting with
    | SessionViewPage _, false -> { model with Exiting = true }, Cmd.ofMsg (SessionViewMsg SessionView.GoBack)
    | _ ->
      exitEvent.Set()
      model, []

let subscriptions (model: Model) =
  match model.Page with
  | SessionListPage -> SessionList.subscriptions model.SessionList |> Subs.map SessionListMsg
  | SessionViewPage vm -> SessionView.subscriptions vm |> Subs.map SessionViewMsg

type AppView(model: Model) =
  interface IWidget with
    member _.Render(ctx: RenderContext) =
      let panels = buildPanels model
      let slotPort = AppLayout.portFor ctx.Viewport (AppLayout.mainLayout model.LogVisible)

      for panel in panels do
        let composedWidget =
          { new IWidget with
              member _.Render(ctx) =
                match panel.Boxed with
                | true ->
                  let port = AppLayout.portFor ctx.Viewport AppLayout.panelInnerLayout
                  ctx.Render(panel.Widget, port AppLayout.Content)
                  ctx.Render(help [ panel.KeyMap ] |> leftAligned, port AppLayout.Keys)
                | false -> ctx.Render(panel.Widget, ctx.Viewport)
          }

        let renderedPanel: IWidget =
          match panel.Boxed with
          | true ->
            let focusState =
              match panel.CapturesInput, panel.Focused with
              | true, _ -> Capturing
              | _, true -> Focused
              | _ -> Unfocused

            focusableBox panel.Title panel.Number focusState composedWidget :> IWidget
          | false -> composedWidget

        ctx.Render(renderedPanel, slotPort panel.LayoutSlot)

      match model.LogVisible with
      | true -> Log.view model.LogModel ctx (slotPort AppLayout.Log)
      | false -> ()

      let helpMaps =
        match model.Page with
        | SessionViewPage viewModel ->
          [ SessionView.keyMap viewModel ]
          @ SessionView.helpKeyMaps viewModel
          @ [ globalKeyMap ]
        | _ -> [ globalKeyMap ]

      ctx.Render(help helpMaps |> leftAligned, slotPort AppLayout.Help)

// Spectre.Tui's AnsiTerminal keeps mutable buffer/state shared across writes
// and is not thread-safe. Subscription callbacks (Firebase observables, async
// completions) can dispatch from thread-pool threads, so view may be invoked
// concurrently. Serialize draws here.
let private renderLock = obj ()

let view (renderer: Renderer) (model: Model) _dispatch =
  lock renderLock (fun () -> renderer.Draw(fun ctx _ -> ctx.Render(AppView model)))

let traceToLog msg (model: Model) _ =
  match msg with
  | Tick -> ()
  | _ -> Log.append (sprintf "%A" msg) model.LogModel
