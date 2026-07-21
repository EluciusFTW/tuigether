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
  HandlePaste: string -> Msg option
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

// Not private: consumed by the AppView rendering module (help bar) and below.
let globalKeyMap: IKeyMap = Keymap.KeyBinding.toKeyMap globalBindings ()

// Not private: buildPanels drives both input routing (here) and rendering (AppView).
let buildPanels (model: Model) : Panel list =
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
        HandlePaste = fun text -> SessionList.handlePaste text model.SessionList |> Option.map SessionListMsg
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
        HandlePaste = fun text -> SessionView.handlePaste text viewModel |> Option.map SessionViewMsg
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
    (fun () -> SessionOrchestration.leaveAndFinalize client sessionId user wasStarted wasDriver)
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
      // Ctrl+V reads the OS clipboard and inserts it in one block via the
      // focused editor's HandlePaste; any other key takes the normal path.
      match TextEditing.isPasteKey key with
      | true ->
        match Clipboard.read () with
        | Ok text ->
          focusedPanel
          |> Option.bind (fun p -> p.HandlePaste text)
          |> Option.map (fun msg -> model, Cmd.ofMsg msg)
          |> Option.defaultValue (model, [])
        | Error message ->
          Log.line (sprintf "clipboard paste failed: %s" message)
          model, []
      | false ->
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

let traceToLog msg (model: Model) _ =
  match msg with
  | Tick -> ()
  | _ -> Log.append (sprintf "%A" msg) model.LogModel
