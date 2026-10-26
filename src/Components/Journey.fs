module Journey

open System
open Dependencies
open Elmish
open Spectre.Console
open Spectre.Tui
open SpectreTuff
open SpectreTuff.Layout
open SpectreTuff.Widgets

let private avatarColors = [|
  Color.Green
  Color.Blue
  Color.Purple
  Color.Aqua
  Color.Red
  Color.Yellow
  Color.Orange1
  Color.Fuchsia
  Color.Maroon
  Color.SkyBlue1
|]

let private colorByName (name: string) =
  match String.IsNullOrWhiteSpace name with
  | true -> Color.Silver
  | false -> avatarColors.[abs (name.GetHashCode()) % avatarColors.Length]

let resolveName () =
  Environment.GetEnvironmentVariable("TUIGETHER_AVATAR")
  |> Option.ofObj
  |> Option.defaultValue ""

type User = { Name: string; Color: Color }

type Model = {
  Users: User list
  ActiveDriver: User option
  CurrentUser: User
  Timer: Timer.Model
  SessionId: string
  Backend: Backend.Backend
}

type Msg =
  | UpdateSession of Session.Data
  | RemoteUserChanged of user: string * presence: Session.UserPresence
  | RemoteUserRemoved of user: string
  | ClearDriver
  | DriverCleared
  | SwitchDriver
  | DriverRotated
  | TimerMsg of Timer.Msg

let private upsertUser (userName: string) (presence: Session.UserPresence) (model: Model) : Model =
  let avatarName: string =
    match isNull (presence :> obj) with
    | true -> null
    | false -> presence.Avatar

  let updated =
    match userName = model.CurrentUser.Name with
    | true -> model.CurrentUser
    | false -> {
        Name = userName
        Color = colorByName avatarName
      }

  let users =
    match model.Users |> List.exists (fun u -> u.Name = userName) with
    | true -> model.Users |> List.map (fun u -> if u.Name = userName then updated else u)
    | false -> model.Users @ [ updated ]

  {
    model with
        Users = users
        ActiveDriver =
          match model.ActiveDriver with
          | None -> None
          | Some d when d.Name = userName -> Some updated
          | other -> other
  }

let private removeUser (userName: string) (model: Model) : Model =
  match userName = model.CurrentUser.Name with
  | true -> model
  | false -> {
      model with
          Users = model.Users |> List.filter (fun u -> u.Name <> userName)
          ActiveDriver =
            match model.ActiveDriver with
            | Some d when d.Name = userName -> None
            | other -> other
    }

let init (deps: Dependencies) (sessionId: string) (currentUser: string) (avatarName: string) (data: Session.Data) =
  let me = {
    Name = currentUser
    Color = colorByName avatarName
  }

  {
    Users = [ me ]
    ActiveDriver =
      match String.IsNullOrWhiteSpace(data.ActiveDriver) with
      | true -> None
      | false ->
        Some {
          Name = data.ActiveDriver
          Color = colorByName data.ActiveDriver
        }
    CurrentUser = me
    Timer = Timer.init deps.Backend sessionId currentUser
    SessionId = sessionId
    Backend = deps.Backend
  }

let private feedTimer (deps: Dependencies) (model: Model) : Timer.Model * Cmd<Msg> =
  let connectedUsers = model.Users |> List.map (fun u -> u.Name)
  let activeDriver = model.ActiveDriver |> Option.map (fun u -> u.Name)
  let userAvatarMap = model.Users |> List.map (fun u -> u.Name, u.Color) |> Map.ofList

  let timerM, timerCmd =
    Timer.update deps (Timer.SessionUpdated(connectedUsers, activeDriver, userAvatarMap)) model.Timer

  timerM, Cmd.map TimerMsg timerCmd

let update (deps: Dependencies) msg model =
  match msg with
  | UpdateSession data ->
    let withDriver = {
      model with
          ActiveDriver =
            match String.IsNullOrWhiteSpace(data.ActiveDriver) with
            | true -> None
            | false -> model.Users |> List.tryFind (fun u -> u.Name = data.ActiveDriver)
    }

    let timerM, timerCmd = feedTimer deps withDriver
    { withDriver with Timer = timerM }, timerCmd

  | RemoteUserChanged(user, presence) -> upsertUser user presence model, []
  | RemoteUserRemoved user -> removeUser user model, []

  | ClearDriver ->
    model,
    Cmd.OfAsync.perform (fun () -> model.Backend.Driving.ClearDriver model.SessionId) () (fun () -> DriverCleared)
  | DriverCleared -> model, []
  | DriverRotated -> model, []

  | SwitchDriver ->
    let users = model.Users

    let nextUser =
      match model.ActiveDriver with
      | None -> users |> List.tryHead
      | Some current ->
        let idx =
          users
          |> List.tryFindIndex (fun u -> u.Name = current.Name)
          |> Option.defaultValue -1

        Some users.[(idx + 1) % users.Length]

    let nextDriverName = nextUser |> Option.map (fun u -> u.Name)
    let connectedNames = users |> List.map (fun u -> u.Name)
    let avatarMap = users |> List.map (fun u -> u.Name, u.Color) |> Map.ofList

    let timerM, timerCmd =
      Timer.resetForDriver model.Timer nextDriverName connectedNames avatarMap
      |> Timer.startDrive deps

    let m = {
      model with
          ActiveDriver = nextUser
          Timer = timerM
    }

    // A prior Skip or Finish already ended the outgoing Drive, unless it still runs or is paused.
    let endsCurrentDrive =
      match model.Timer.State with
      | Timer.Running
      | Timer.Paused -> true
      | _ -> false

    let currentDriver = model.ActiveDriver |> Option.map (fun u -> u.Name)

    // SwitchDriver runs only on the Client that pressed the key, so the Drive history gets one entry.
    let rotateCmd =
      Cmd.OfAsync.perform
        (fun () ->
          model.Backend.Driving.RotateDriver
            model.SessionId
            model.CurrentUser.Name
            currentDriver
            endsCurrentDrive
            nextDriverName
            (Timer.toSnapshot timerM))
        ()
        (fun () -> DriverRotated)

    m, Cmd.batch [ rotateCmd; Cmd.map TimerMsg timerCmd ]

  | TimerMsg tMsg ->
    let m, cmd = Timer.update deps tMsg model.Timer
    { model with Timer = m }, Cmd.map TimerMsg cmd

let subscriptions (model: Model) =
  (model.Backend.Presence.Subscription model.SessionId "journey"
   |> Subs.map (fun ev ->
     match ev with
     | Backend.UserChanged(user, presence) -> RemoteUserChanged(user, presence)
     | Backend.UserRemoved user -> RemoteUserRemoved user))
  @ (Timer.subscriptions model.Timer |> Subs.map TimerMsg)

let keyMap (_model: Model) : Spectre.Tui.App.IKeyMap =
  { new Spectre.Tui.App.IKeyMap with
      member _.Help() =
        Seq.empty
  }

let private journeyLayout =
  layout "journey"
  |> splitVertically [|
    layout "pad-left" |> withFixedSize (Some 1)
    layout "users" |> withFixedSize (Some 20)
    layout "road"
    layout "pad-right" |> withFixedSize (Some 1)
  |]

let widget (model: Model) : IWidget =
  { new IWidget with
      member _.Render(context: RenderContext) =
        let port = getPort context.Viewport journeyLayout

        let userLine (user: User) =
          let color = user.Color
          let box = Text.styledSpan (Nullable(Style color)) "██"

          let isDriver =
            match model.ActiveDriver with
            | Some d -> d.Name = user.Name
            | None -> false

          let nameSpan =
            match isDriver with
            | true -> Text.styledSpan (Nullable(Style color)) (sprintf "▶ %s" user.Name)
            | false -> Text.span (sprintf "  %s" user.Name)

          Text.line [ box; Text.span " "; nameSpan ]

        let userLines = model.Users |> List.map userLine

        context.Render(paragraph userLines |> withOverflow Overflow.Ellipsis, port "users")

        context.Render(Timer.widget model.Timer, port "road")
  }
