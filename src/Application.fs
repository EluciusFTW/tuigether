module Application

open System
open Dependencies
open Elmish
open Firebase.Database
open Spectre.Tui
open Spectre.Tui.App
open SpectreTuff
open SpectreTuff.Elmish
open SpectreTuff.Layout
open SpectreTuff.Widgets
open Tuisweeper
open Tui48
open Tuiboom

/// The games that can be opened on top of the app; at most one at a time.
type Ingame =
  | SweeperGame
  | TileGame
  | ArenaGame

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
  ShowKeymap: bool
  Sweeper: Tuisweeper.Model
  Tile: Tui48.Model
  /// Only exists inside a session: Tuiboom is played against someone in it.
  Arena: Tuiboom.Model option
  OpenGame: Ingame option
  /// The last challenge we notified about, so each one notifies once.
  NotifiedChallenge: string
  Exiting: bool
}

type Msg =
  | InputMsg of ConsoleKeyInfo
  | SessionListMsg of SessionList.Msg
  | SessionViewMsg of SessionView.Msg
  | LeaveFinalized
  | ToggleLog
  | ToggleKeymap
  | ToggleGame of Ingame
  | SweeperMsg of Tuisweeper.Msg
  | TileMsg of Tui48.Msg
  | ArenaMsg of Tuiboom.Msg
  | ArenaChallenge of MatchInfo option
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

// The global keys' behavior and their help-bar documentation are derived from a single
// binding list, so a key and its description can never drift apart.
let private globalBindings: Keymap.KeyBinding<Model, Msg> list = [
  Keymap.KeyBinding.create 'q' "quit" Exit
  Keymap.KeyBinding.create 'l' "toggle log" ToggleLog
  Keymap.KeyBinding.create 't' "tuisweeper" (ToggleGame SweeperGame)
  Keymap.KeyBinding.create 'g' "tui48" (ToggleGame TileGame)
  Keymap.KeyBinding.dynamic (Keymap.CharKey 'b') (fun model -> {
    Description = "tuiboom"
    Message =
      match model.Page with
      | SessionViewPage _ -> Some(ToggleGame ArenaGame)
      | SessionListPage -> None
  })
  Keymap.KeyBinding.dynamic (Keymap.CharKey '?') (fun _ -> {
    Description = "keymaps"
    Message =
      match KeymapModal.mode with
      | KeymapModal.Modal -> Some ToggleKeymap
      | KeymapModal.Inline -> None
  })
]

let private handleGlobalKey (key: ConsoleKeyInfo) (model: Model) : Msg option =
  Keymap.KeyBinding.handleKey globalBindings key model

// Not private: consumed by the AppView rendering module (help bar) and below.
let globalKeyMap (model: Model) : IKeyMap =
  Keymap.KeyBinding.toKeyMap globalBindings model

let keymapHintMap: IKeyMap = Keymap.KeyBinding.toKeyMap [ Keymap.KeyBinding.create '?' "keymaps" ToggleKeymap ] ()

let keymapSections (model: Model) : (string * IKeyMap) list =
  let pageSections =
    match model.Page with
    | SessionListPage -> [ "Sessions", SessionList.keyMap model.SessionList ]
    | SessionViewPage viewModel -> SessionView.keymapSections viewModel

  pageSections @ [ "Global", globalKeyMap model ]

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

// Both game packages have a Highscore module, so these stay qualified.
let private sweeperDeps = Tuisweeper.Dependencies.create Tuisweeper.Highscore.Store.defaultFile
let private tileDeps = Tui48.Dependencies.create Tui48.Highscore.Store.defaultFile

let init (client: FirebaseClient) (user: string) () =
  let listModel, listCmd = SessionList.init client user ()

  let avatarName = Journey.resolveName ()
  let sweeperModel, sweeperCmd = Tuisweeper.init sweeperDeps
  let tileModel, tileCmd = Tui48.init tileDeps

  {
    Page = SessionListPage
    SessionList = listModel
    User = user
    AvatarName = avatarName
    Focus = 1
    LogVisible = false
    LogModel = Log.init ()
    ShowKeymap = false
    Sweeper = sweeperModel
    Tile = tileModel
    Arena = None
    OpenGame = None
    NotifiedChallenge = ""
    Exiting = false
  },
  Cmd.batch [
    Cmd.map SessionListMsg listCmd
    Cmd.map SweeperMsg sweeperCmd
    Cmd.map TileMsg tileCmd
  ]

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
    // The match itself is forfeited during leave finalization.
    let openGame =
      match model.OpenGame with
      | Some ArenaGame -> None
      | other -> other

    {
      model with
          Page = SessionListPage
          Arena = None
          OpenGame = openGame
    },
    leaveFinalizeCmd client sessionId user wasStarted wasDriver
  | None -> model, []

let update (deps: Dependencies) (user: string) msg model =
  match msg with
  | InputMsg key ->
    match model.OpenGame, model.ShowKeymap with
    // While a game is open it owns the keyboard (it closes itself on q/Esc); its own key toggles it from outside.
    | Some SweeperGame, _ ->
      match key.KeyChar with
      | 't' -> model, Cmd.ofMsg (ToggleGame SweeperGame)
      | _ -> model, Cmd.ofMsg (SweeperMsg(Tuisweeper.InputMsg key))
    | Some TileGame, _ ->
      match key.KeyChar with
      | 'g' -> model, Cmd.ofMsg (ToggleGame TileGame)
      | _ -> model, Cmd.ofMsg (TileMsg(Tui48.InputMsg key))
    | Some ArenaGame, _ ->
      match key.KeyChar with
      | 'b' -> model, Cmd.ofMsg (ToggleGame ArenaGame)
      | _ -> model, Cmd.ofMsg (ArenaMsg(Tuiboom.InputMsg key))
    | None, true ->
      match key.Key, key.KeyChar with
      | ConsoleKey.Escape, _
      | _, '?'
      | _, 'q' -> model, Cmd.ofMsg ToggleKeymap
      | _ -> model, []
    | None, false ->

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
        match handleGlobalKey key model with
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
    | true -> model, TuiCmd.exit
    | false -> model, []

  | ToggleLog ->
    {
      model with
          LogVisible = not model.LogVisible
    },
    []

  | ToggleKeymap ->
    {
      model with
          ShowKeymap = not model.ShowKeymap
    },
    []

  | ToggleGame game ->
    let openGame =
      match model.OpenGame with
      | Some current when current = game -> None
      | _ -> Some game

    // Tuiboom is created on first open in a session and kept while it's hidden, so a
    // match survives toggling (it pauses meanwhile: no ticks, no packets).
    match openGame, model.Page, model.Arena with
    | Some ArenaGame, SessionViewPage viewModel, None ->
      let arenaModel, arenaCmd = Tuiboom.init (Firebase.Arena.transport deps.Client viewModel.SessionId user)

      {
        model with
            OpenGame = openGame
            Arena = Some arenaModel
      },
      Cmd.map ArenaMsg arenaCmd
    | Some ArenaGame, SessionListPage, _ -> model, []
    | _ -> { model with OpenGame = openGame }, []

  | SweeperMsg sMsg ->
    let sweeperModel, sweeperCmd, outMsg = Tuisweeper.update sweeperDeps sMsg model.Sweeper
    let model = { model with Sweeper = sweeperModel }
    let sweeperCmd = Cmd.map SweeperMsg sweeperCmd

    match outMsg with
    | Some Tuisweeper.Closed -> { model with OpenGame = None }, sweeperCmd
    | None -> model, sweeperCmd

  | TileMsg tMsg ->
    // Keep the cmd along with Closed: it saves the highscore of the game in progress.
    let tileModel, tileCmd, outMsg = Tui48.update tileDeps tMsg model.Tile
    let model = { model with Tile = tileModel }
    let tileCmd = Cmd.map TileMsg tileCmd

    match outMsg with
    | Some Tui48.Closed -> { model with OpenGame = None }, tileCmd
    | None -> model, tileCmd

  | ArenaMsg aMsg ->
    match model.Arena with
    | Some arenaModel ->
      // Keep the cmd along with Closed: it writes a forfeit or a withdrawn challenge.
      let arenaModel, arenaCmd, outMsg = Tuiboom.update aMsg arenaModel
      let arenaCmd = Cmd.map ArenaMsg arenaCmd

      match outMsg with
      | Some Tuiboom.Closed ->
        {
          model with
              Arena = None
              OpenGame = None
        },
        arenaCmd
      | None -> { model with Arena = Some arenaModel }, arenaCmd
    | None -> model, []

  | ArenaChallenge(Some info) when
    info.Status = MatchStatus.Open
    && info.Host <> user
    && info.MatchId <> model.NotifiedChallenge
    ->
    deps.Notify(sprintf "%s challenges you to Tuiboom (press b)" info.Host)
    Log.line (sprintf "%s challenges you to Tuiboom (press b)" info.Host)

    {
      model with
          NotifiedChallenge = info.MatchId
    },
    []
  | ArenaChallenge _ -> model, []

  | Tick -> model, []

  | Exit ->
    match model.Page, model.Exiting with
    | SessionViewPage _, false -> { model with Exiting = true }, Cmd.ofMsg (SessionViewMsg SessionView.GoBack)
    | _ -> model, TuiCmd.exit

let subscriptions (model: Model) =
  let pageSubs =
    match model.Page with
    | SessionListPage -> SessionList.subscriptions model.SessionList |> Subs.map SessionListMsg
    | SessionViewPage vm -> SessionView.subscriptions vm |> Subs.map SessionViewMsg

  // Elmish's Sub.map prefixes the ids: the games' generic ids (e.g. "tick") must not clash with ours.
  let gameSubs =
    match model.OpenGame with
    | Some SweeperGame -> Tuisweeper.subscriptions model.Sweeper |> Sub.map "tuisweeper" SweeperMsg
    | Some TileGame -> Tui48.subscriptions model.Tile |> Sub.map "tui48" TileMsg
    | Some ArenaGame ->
      model.Arena
      |> Option.map (Tuiboom.subscriptions >> Sub.map "tuiboom" ArenaMsg)
      |> Option.defaultValue []
    | None -> []

  // While Tuiboom is closed, watch the session's match to notice challenges.
  let challengeSubs =
    match model.Page, model.OpenGame with
    | SessionViewPage vm, openGame when openGame <> Some ArenaGame ->
      Firebase.Arena.matchSubscription vm.Client vm.SessionId ArenaChallenge
    | _ -> []

  pageSubs @ gameSubs @ challengeSubs

let traceToLog msg (model: Model) _ =
  match msg with
  | Tick
  | SweeperMsg Tuisweeper.Tick
  | ArenaMsg Tuiboom.Tick
  | ArenaMsg(Tuiboom.PacketReceived _)
  | ArenaMsg(Tuiboom.PacketSent _) -> ()
  | _ -> Log.append (sprintf "%A" msg) model.LogModel
