module Timer

open System
open Dependencies
open Elmish
open Firebase.Database
open Spectre.Console
open Spectre.Tui
open SpectreTuff
open SpectreTuff.Widgets

// ─── Types ───────────────────────────────────────────────────────────────────

type Phase =
  | Work
  | Break

type TimerState =
  | Idle
  | Running
  | Paused
  | Flashing of int
  | Breaking of int

type Persistence = {
  Client: FirebaseClient
  SessionId: string
}

type Model = {
  Remaining: TimeSpan
  Phase: Phase
  State: TimerState
  ActiveDriver: string option
  ConnectedUsers: string list
  UserAvatars: Map<string, Color>
  TickEpoch: int
  Persistence: Persistence
  // Absolute instant (unix ms) the running Work countdown reaches zero. Drives
  // the displayed Remaining so all clients agree regardless of tick jitter.
  EndsAt: int64 option
  // Local user, used to attribute auto-finish history to the driver's client only.
  CurrentUser: string
}

type Msg =
  | Start
  | Stop
  | Pause
  | Tick of int
  | Reset
  | SwitchDriver
  | SkipTimer
  | SkipPause
  | WorkFinished
  | FlashTick
  | StartBreak
  | BreakTick
  | BreakFinished
  | SessionUpdated of string list * string option * Map<string, Color>
  | RemoteStateLoaded of Session.TimerState option
  | StateSaved
  | HistoryAppended

// ─── Constants ───────────────────────────────────────────────────────────────

let private workDuration = TimeSpan.FromMinutes 25.0
let private breakDuration = TimeSpan.FromMinutes 5.0
let private flashFrameCount = 6

// ─── Commands ────────────────────────────────────────────────────────────────

let private tickCmd (epoch: int) =
  Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep 1000 }) () (fun () -> Tick epoch)

let private flashTickCmd = Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep 200 }) () (fun () -> FlashTick)

let private breakTickCmd = Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep 500 }) () (fun () -> BreakTick)

// ─── Init ────────────────────────────────────────────────────────────────────

let init (client: FirebaseClient) (sessionId: string) (currentUser: string) = {
  Remaining = workDuration
  Phase = Work
  State = Idle
  ActiveDriver = None
  ConnectedUsers = []
  UserAvatars = Map.empty
  TickEpoch = 0
  Persistence = {
    Client = client
    SessionId = sessionId
  }
  EndsAt = None
  CurrentUser = currentUser
}

let resetForDriver (previous: Model) (driver: string option) (users: string list) (avatars: Map<string, Color>) = {
  Remaining = workDuration
  Phase = Work
  State = Idle
  ActiveDriver = driver
  ConnectedUsers = users
  UserAvatars = avatars
  TickEpoch = previous.TickEpoch + 1
  Persistence = previous.Persistence
  EndsAt = None
  CurrentUser = previous.CurrentUser
}

// ─── Persistence ─────────────────────────────────────────────────────────────

let private nowMs () = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

let private toTimerState (model: Model) : Session.TimerState = {
  RemainingSeconds = int model.Remaining.TotalSeconds
  IsRunning = model.State = Running
  EndsAt = model.EndsAt |> Option.defaultValue 0L
}

let private saveCmd (model: Model) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> Firebase.Timer.save model.Persistence.Client model.Persistence.SessionId (toTimerState model))
    ()
    (fun () -> StateSaved)

// Append a structured drive-history event. Callers must ensure exactly one client
// writes a given event (local user actions are inherently single-writer; auto-finish
// is gated to the active driver's client) so the append-only log holds no duplicates.
let private historyCmd (model: Model) (eventType: Session.DriveEventType) : Cmd<Msg> =
  match model.ActiveDriver with
  | Some driver ->
    Cmd.OfAsync.perform
      (fun () ->
        Firebase.History.append model.Persistence.Client model.Persistence.SessionId {
          Session.DriveEvent.Type = Session.DriveEventType.toString eventType
          Session.DriveEvent.Driver = driver
          Session.DriveEvent.By = model.CurrentUser
          Session.DriveEvent.At = nowMs ()
        })
      ()
      (fun () -> HistoryAppended)
  | None -> Cmd.none

// ─── Update ──────────────────────────────────────────────────────────────────

let update (deps: Dependencies) msg model =
  match msg with
  | Start ->
    match model.State with
    | Idle
    | Paused ->
      match model.State with
      | Idle -> deps.Notify "Work started!"
      | _ -> deps.Notify "Work resumed"

      let epoch = model.TickEpoch + 1
      let endsAt = nowMs () + int64 model.Remaining.TotalMilliseconds

      let m = {
        model with
            State = Running
            TickEpoch = epoch
            EndsAt = Some endsAt
      }

      // No history here: the only path to a Work start is Journey.SwitchDriver
      // (local-only), which logs the Started event. Start is also dispatched when
      // adopting remote state, so logging here would duplicate across clients.
      m, Cmd.batch [ tickCmd epoch; saveCmd m ]
    | _ -> model, []
  | Stop ->
    deps.Notify "Timer stopped"

    let m = {
      model with
          State = Idle
          TickEpoch = model.TickEpoch + 1
          EndsAt = None
    }

    m, Cmd.batch [ saveCmd m; historyCmd model Session.DriveEventType.Stopped ]
  | Pause ->
    match model.State with
    | Running ->
      deps.Notify "Paused"

      let m = {
        model with
            State = Paused
            TickEpoch = model.TickEpoch + 1
            EndsAt = None
      }

      m, saveCmd m
    | _ -> model, []
  | Tick epoch when epoch <> model.TickEpoch -> model, []
  | Tick _ ->
    match model.State, model.EndsAt with
    | Running, Some endsAt ->
      // Recompute from the shared deadline rather than decrementing, so the
      // display tracks EndsAt exactly and cannot drift between clients.
      let remainingMs = endsAt - nowMs ()

      if remainingMs <= 0L then
        { model with Remaining = TimeSpan.Zero }, Cmd.ofMsg WorkFinished
      else
        {
          model with
              Remaining = TimeSpan.FromMilliseconds(float remainingMs)
        },
        tickCmd model.TickEpoch
    | _ -> model, []
  | WorkFinished ->
    deps.Notify "Drive finished — driver change, break started!"

    let m = {
      model with
          State = Flashing flashFrameCount
          EndsAt = None
    }

    // WorkFinished fires independently on every client when the shared deadline
    // passes, so gate the history write to the active driver's client to avoid
    // duplicate log entries. The drift-free EndsAt makes that instant agree.
    let logCmd =
      match model.ActiveDriver with
      | Some driver when driver = model.CurrentUser -> historyCmd model Session.DriveEventType.Finished
      | _ -> Cmd.none

    m, Cmd.batch [ flashTickCmd; saveCmd m; logCmd ]
  | SkipTimer ->
    match model.State with
    | Running
    | Paused ->
      deps.Notify "Drive skipped — driver change, break started!"

      let m = {
        model with
            State = Flashing flashFrameCount
            Remaining = TimeSpan.Zero
            EndsAt = None
      }

      m, Cmd.batch [ flashTickCmd; saveCmd m; historyCmd model Session.DriveEventType.Skipped ]
    | _ -> model, []
  | FlashTick ->
    match model.State with
    | Flashing n when n > 0 -> { model with State = Flashing(n - 1) }, flashTickCmd
    | Flashing _ -> model, Cmd.ofMsg StartBreak
    | _ -> model, []
  | StartBreak ->
    {
      model with
          State = Breaking 0
          Phase = Break
          Remaining = breakDuration
    },
    breakTickCmd
  | BreakTick ->
    match model.State with
    | Breaking frame ->
      let next = model.Remaining - TimeSpan.FromSeconds 0.5

      if next <= TimeSpan.Zero then
        { model with Remaining = TimeSpan.Zero }, Cmd.ofMsg BreakFinished
      else
        {
          model with
              State = Breaking(frame + 1)
              Remaining = next
        },
        breakTickCmd
    | _ -> model, []
  | BreakFinished ->
    deps.Notify "Break over!"

    let m = {
      model with
          State = Idle
          Phase = Work
          Remaining = workDuration
          EndsAt = None
    }

    m, saveCmd m
  | SkipPause ->
    match model.State with
    | Breaking _ ->
      deps.Notify "Break skipped!"

      let m = {
        model with
            State = Idle
            Phase = Work
            Remaining = workDuration
            EndsAt = None
      }

      m, saveCmd m
    | _ -> model, []
  | Reset ->
    match model.State with
    | Idle
    | Paused ->
      {
        model with
            Remaining = workDuration
            Phase = Work
            State = Idle
            EndsAt = None
      },
      []
    | _ -> model, []
  | SwitchDriver -> model, []
  | SessionUpdated(users, driver, avatars) ->
    {
      model with
          ConnectedUsers = users
          ActiveDriver = driver
          UserAvatars = avatars
    },
    []
  | RemoteStateLoaded(Some state) ->
    // Adopt the remote authoritative state directly. Crucially we do NOT dispatch
    // Start/Pause here: those would recompute EndsAt and re-save, pushing the
    // deadline later on every client and echoing writes back and forth. Instead
    // we take the remote EndsAt verbatim and only spin up a local display tick.
    let remainingFromDeadline () =
      TimeSpan.FromMilliseconds(float (max 0L (state.EndsAt - nowMs ())))

    match state.IsRunning, model.State with
    | true, (Idle | Paused) ->
      let epoch = model.TickEpoch + 1

      {
        model with
            State = Running
            EndsAt = Some state.EndsAt
            Remaining = remainingFromDeadline ()
            TickEpoch = epoch
      },
      tickCmd epoch
    | true, Running ->
      // Already ticking; just re-anchor to the remote deadline in case it moved.
      {
        model with
            EndsAt = Some state.EndsAt
            Remaining = remainingFromDeadline ()
      },
      Cmd.none
    | false, Running ->
      {
        model with
            State = Paused
            EndsAt = None
            Remaining = TimeSpan.FromSeconds(float state.RemainingSeconds)
            TickEpoch = model.TickEpoch + 1
      },
      Cmd.none
    | false, (Idle | Paused) ->
      {
        model with
            Remaining = TimeSpan.FromSeconds(float state.RemainingSeconds)
      },
      Cmd.none
    | _ -> model, []
  | RemoteStateLoaded None -> model, []
  | StateSaved -> model, []
  | HistoryAppended -> model, []

let subscriptions (model: Model) =
  Firebase.Timer.subscription model.Persistence.Client model.Persistence.SessionId RemoteStateLoaded

// ─── Widget ──────────────────────────────────────────────────────────────────

let private formatTime (t: TimeSpan) =
  sprintf "%02d:%02d" (int t.TotalMinutes) t.Seconds

// Road grid: N columns × 2 rows (N derived from viewport width at render time)
//
// R0:  .  .  .  .  [head]  .  .  .  .  .  .   sky row + driver head (car middle)
// R1:  ═  ═  ═  [whl][base][whl]  ─  ─  ─  ─   road + car wheels
//
// carPos ∈ [0, roadWidth-3]:  car occupies cols carPos..carPos+2
// cols < carPos  → filled road     cols > carPos+2 → unfilled road

let private carWidth = 3

let private styledBlock (color: Color) =
  Text.styledSpan (Nullable(Style color)) "██"

let private emptyBlock = Text.span "  "

// Pause glyph (big "||"): two vertical bars, 3 rows tall. The Timer gets only
// 4 rows here (journey height 7, minus box border and the panel keys strip), so
// glyph + timer line fill it exactly. Leading empty cell aligns with the "  " margin.

let private pauseLines =
  let e = emptyBlock
  let P = styledBlock Color.DeepSkyBlue1

  [ Text.line [ e; P; P; e; P; P ]
    Text.line [ e; P; P; e; P; P ]
    Text.line [ e; P; P; e; P; P ] ]

let widget (model: Model) : IWidget =
  { new IWidget with
      member _.Render(context: RenderContext) =
        match model.State with
        | Breaking _ ->
          let infoLine = Text.line [ Text.span (sprintf "  BREAK  %s" (formatTime model.Remaining)) ]

          context.Render(paragraph (pauseLines @ [ infoLine ]), context.Viewport)

        | _ ->
          let roadWidth = max carWidth (context.Viewport.Width / 2)

          let driverColor =
            model.ActiveDriver
            |> Option.bind (fun u -> model.UserAvatars |> Map.tryFind u)

          let filledColor =
            match model.State with
            | Running -> Color.Green
            | Paused -> Color.Yellow
            | Flashing n when n % 2 = 0 -> Color.Red
            | Flashing _ -> Color.Yellow1
            | _ -> Color.Grey35

          let totalSecs =
            match model.Phase with
            | Work -> workDuration.TotalSeconds
            | Break -> breakDuration.TotalSeconds

          let progress = 1.0 - model.Remaining.TotalSeconds / totalSecs

          let carPos =
            match model.State with
            | Flashing _ -> roadWidth - carWidth
            | _ -> min (roadWidth - carWidth) (int (progress * float (roadWidth - carWidth)))

          let isFinish =
            match model.State with
            | Flashing _ -> true
            | _ -> false

          let roadCell row col =
            let inCar = col >= carPos && col < carPos + carWidth
            let filled = col < carPos
            let finish = isFinish && col = roadWidth - 1
            let isHead = col - carPos = 1

            let roadSurface =
              match filled with
              | true -> styledBlock filledColor
              | false -> styledBlock Color.Grey23

            match row with
            | 0 ->
              match inCar && isHead, finish with
              | true, _ -> styledBlock (driverColor |> Option.defaultValue Color.Silver)
              | false, true -> styledBlock Color.White
              | false, false -> emptyBlock
            | _ ->
              match finish, inCar, isHead with
              | true, _, _ -> styledBlock Color.White
              | false, true, true -> styledBlock Color.Grey
              | false, true, false -> styledBlock Color.Grey3
              | false, false, _ -> roadSurface

          let stateStr =
            match model.State with
            | Running -> "▶"
            | Paused -> "||"
            | Flashing _ -> "!!!"
            | _ -> "■"

          let roadLines = [
            for row in 0..1 -> Text.line [ for col in 0 .. (roadWidth - 1) -> roadCell row col ]
          ]

          let infoLine = Text.line [ Text.span (sprintf "  %s %s" stateStr (formatTime model.Remaining)) ]

          context.Render(paragraph (roadLines @ [ infoLine ]), context.Viewport)
  }
