module Tuiboom.Tuiboom

open System
open Elmish
open Spectre.Console
open Spectre.Tui
open Spectre.Tui.App
open SpectreTuff
open SpectreTuff.Elmish
open SpectreTuff.Widgets
open Tuiboom.Game

// With no packet from the opponent for this long, the match is abandoned in our favour.
let private disconnectTimeoutMs = 30_000L

/// A match this player takes part in, from the moment both players are in.
type Running = {
  Info: MatchInfo
  Net: Netcode.Net
  LastHeardAt: int64
  /// Set once this client has written the match's end (Over/Abandoned).
  ReportedEnd: bool
}

type Model = {
  Transport: Transport
  /// The session's current match, as last seen; None until the first load.
  Match: MatchInfo option
  Loaded: bool
  Running: Running option
  /// The match whose own earlier packets are being loaded before it (re)starts.
  Joining: string option
  ConfirmForfeit: bool
  Error: string option
}

type Msg =
  | InputMsg of ConsoleKeyInfo
  | Tick
  | MatchChanged of MatchInfo option
  | Steer of Dir
  | DropBomb
  | Confirm
  | Quit
  | OwnPacketsLoaded of matchId: string * WirePacket list
  | PacketReceived of matchId: string * WirePacket
  | PacketSent of matchId: string * Result<unit, string>
  | Saved of Result<unit, string>

type OutMsg = Closed

let private bindings: Keymap.KeyBinding<Model, Msg> list = [
  Keymap.KeyBinding.createSpecial ConsoleKey.UpArrow "up" (Steer Up)
  |> Keymap.KeyBinding.orKey (Keymap.CharKey 'w')
  Keymap.KeyBinding.createSpecial ConsoleKey.DownArrow "down" (Steer Down)
  |> Keymap.KeyBinding.orKey (Keymap.CharKey 's')
  Keymap.KeyBinding.createSpecial ConsoleKey.LeftArrow "left" (Steer Left)
  |> Keymap.KeyBinding.orKey (Keymap.CharKey 'a')
  Keymap.KeyBinding.createSpecial ConsoleKey.RightArrow "right" (Steer Right)
  |> Keymap.KeyBinding.orKey (Keymap.CharKey 'd')
  Keymap.KeyBinding.createSpecial ConsoleKey.Spacebar "bomb" DropBomb
  Keymap.KeyBinding.createSpecial ConsoleKey.Enter "confirm" Confirm
  Keymap.KeyBinding.create 'q' "quit" Quit
  |> Keymap.KeyBinding.orKey (Keymap.SpecialKey ConsoleKey.Escape)
]

let init (transport: Transport) =
  {
    Transport = transport
    Match = None
    Loaded = false
    Running = None
    Joining = None
    ConfirmForfeit = false
    Error = None
  },
  Cmd.none

let private nowMs () =
  DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

let private isPlayer (user: string) (info: MatchInfo) =
  info.Host = user || info.Guest = user

let private opponentOf (user: string) (info: MatchInfo) =
  match info.Host = user with
  | true -> info.Guest
  | false -> info.Host

let private saveCmd (transport: Transport) (info: MatchInfo) =
  Cmd.OfAsync.either transport.SaveMatch info Saved (fun e -> Saved(Error e.Message))

let private isDecided (r: Running) =
  match r.Net.Base.Phase with
  | MatchOver _ -> true
  | _ -> false

// Ticks keep running after the match is decided (status Over), so the opponent still
// receives our final packets and reaches the same end.
let private isLive (r: Running) =
  match r.Info.Status with
  | MatchStatus.Playing
  | MatchStatus.Over -> true
  | _ -> false

/// The end of the match this client should write, if any: the decided result, a lost
/// opponent, or an aborted match after the two simulations diverged.
let private endOf (user: string) (r: Running) =
  match r.Info.Status, r.Net.Base.Phase, r.Net.Desync with
  | MatchStatus.Playing, _, Some _ ->
    Some {
      r.Info with
          Status = MatchStatus.Abandoned
          LeftBy = ""
    }
  | MatchStatus.Playing, MatchOver winner, _ ->
    Some {
      r.Info with
          Status = MatchStatus.Over
          Winner =
            match winner with
            | 0 -> r.Info.Host
            | _ -> r.Info.Guest
    }
  | MatchStatus.Playing, _, _ when nowMs () - r.LastHeardAt > disconnectTimeoutMs ->
    Some {
      r.Info with
          Status = MatchStatus.Abandoned
          LeftBy = opponentOf user r.Info
    }
  | _ -> None

let private tick (model: Model) (r: Running) =
  let transport = model.Transport
  let net, packet = r.Net |> Netcode.advance |> Netcode.takePacket
  let r = { r with Net = net }

  let sendCmd =
    match packet with
    | Some p ->
      Cmd.OfAsync.either
        (fun () -> transport.SendPacket r.Info.MatchId transport.User (Wire.encode p))
        ()
        (fun result -> PacketSent(r.Info.MatchId, result))
        (fun e -> PacketSent(r.Info.MatchId, Error e.Message))
    | None -> Cmd.none

  match r.ReportedEnd, endOf transport.User r with
  | false, Some info ->
    {
      model with
          Running = Some { r with ReportedEnd = true }
    },
    Cmd.batch [ sendCmd; saveCmd transport info ]
  | _ -> { model with Running = Some r }, sendCmd

let private challenge (transport: Transport) =
  let info = {
    MatchId = Guid.NewGuid().ToString "N"
    Host = transport.User
    Guest = ""
    Seed = Random.Shared.Next()
    Status = MatchStatus.Open
    Winner = ""
    LeftBy = ""
  }

  Cmd.OfAsync.either
    (fun () ->
      async {
        do! transport.ClearPackets()
        return! transport.SaveMatch info
      })
    ()
    Saved
    (fun e -> Saved(Error e.Message))

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> * OutMsg option =
  let transport = model.Transport
  let user = transport.User

  match msg with
  | InputMsg key ->
    match model.ConfirmForfeit, Keymap.KeyBinding.handleKey bindings key model with
    | true, Some Quit -> model, Cmd.ofMsg Quit, None
    | true, _ -> { model with ConfirmForfeit = false }, Cmd.none, None
    | false, Some m -> model, Cmd.ofMsg m, None
    | false, None -> model, Cmd.none, None

  | MatchChanged matchInfo ->
    let model = {
      model with
          Match = matchInfo
          Loaded = true
    }

    match matchInfo, model.Running with
    | Some info, Some r when r.Info.MatchId = info.MatchId ->
      {
        model with
            Running = Some { r with Info = info }
      },
      Cmd.none,
      None
    | Some info, _ when info.Status = MatchStatus.Playing && isPlayer user info ->
      match model.Joining with
      | Some id when id = info.MatchId -> model, Cmd.none, None
      | _ ->
        // Load what we already sent in this match: empty for a fresh one, our inputs
        // so far when resuming after a restart.
        {
          model with
              Running = None
              Joining = Some info.MatchId
        },
        Cmd.OfAsync.either
          (fun () -> transport.LoadPackets info.MatchId user)
          ()
          (fun packets -> OwnPacketsLoaded(info.MatchId, packets))
          (fun _ -> OwnPacketsLoaded(info.MatchId, [])),
        None
    | _ -> { model with Running = None }, Cmd.none, None

  | OwnPacketsLoaded(matchId, packets) ->
    match model.Match with
    | Some info when info.MatchId = matchId && model.Joining = Some matchId ->
      let me =
        match info.Host = user with
        | true -> 0
        | false -> 1

      {
        model with
            Joining = None
            Running =
              Some {
                Info = info
                Net = Netcode.start me info.Seed (packets |> List.map Wire.decode)
                LastHeardAt = nowMs ()
                ReportedEnd = false
              }
      },
      Cmd.none,
      None
    | _ -> model, Cmd.none, None

  | Tick ->
    match model.Running with
    | Some r when isLive r ->
      let model, cmd = tick model r
      model, cmd, None
    | _ -> model, Cmd.none, None

  | PacketReceived(matchId, wire) ->
    match model.Running with
    | Some r when r.Info.MatchId = matchId ->
      {
        model with
            Running =
              Some {
                r with
                    Net = Netcode.receive (Wire.decode wire) r.Net
                    LastHeardAt = nowMs ()
              }
      },
      Cmd.none,
      None
    | _ -> model, Cmd.none, None

  | PacketSent(matchId, result) ->
    match model.Running with
    | Some r when r.Info.MatchId = matchId ->
      {
        model with
            Running =
              Some {
                r with
                    Net = Netcode.sent (Result.isOk result) r.Net
              }
      },
      Cmd.none,
      None
    | _ -> model, Cmd.none, None

  | Steer dir ->
    match model.Running with
    | Some r when r.Info.Status = MatchStatus.Playing ->
      {
        model with
            Running =
              Some {
                r with
                    Net = Netcode.act (Move dir) r.Net
              }
      },
      Cmd.none,
      None
    | _ -> model, Cmd.none, None

  | DropBomb ->
    match model.Running with
    | Some r when r.Info.Status = MatchStatus.Playing ->
      {
        model with
            Running =
              Some {
                r with
                    Net = Netcode.act PlaceBomb r.Net
              }
      },
      Cmd.none,
      None
    | _ -> model, Cmd.none, None

  | Confirm ->
    match model.Match, model.Running with
    | _, Some r when r.Info.Status = MatchStatus.Playing -> model, Cmd.none, None
    | Some info, _ when info.Status = MatchStatus.Open && info.Host <> user ->
      model,
      saveCmd transport {
        info with
            Guest = user
            Status = MatchStatus.Playing
      },
      None
    | Some info, _ when info.Status = MatchStatus.Open -> model, Cmd.none, None
    | Some info, _ when info.Status = MatchStatus.Playing -> model, Cmd.none, None
    | _ -> { model with Error = None }, challenge transport, None

  | Quit ->
    match model.Match, model.Running with
    | _, Some r when r.Info.Status = MatchStatus.Playing && not (isDecided r) ->
      match model.ConfirmForfeit with
      | false -> { model with ConfirmForfeit = true }, Cmd.none, None
      | true ->
        model,
        saveCmd transport {
          r.Info with
              Status = MatchStatus.Abandoned
              LeftBy = user
        },
        Some Closed
    | Some info, _ when info.Status = MatchStatus.Open && info.Host = user ->
      // Withdraw the challenge nobody accepted.
      model,
      saveCmd transport {
        info with
            Status = MatchStatus.Abandoned
            LeftBy = user
      },
      Some Closed
    | _ -> model, Cmd.none, Some Closed

  | Saved(Ok()) -> model, Cmd.none, None
  | Saved(Error e) -> { model with Error = Some e }, Cmd.none, None

let subscriptions (model: Model) : Sub<Msg> =
  let transport = model.Transport

  let packets =
    match model.Running with
    | Some r when isLive r ->
      let opponent = opponentOf transport.User r.Info
      let matchId = r.Info.MatchId

      [
        [ "packets"; matchId; opponent ],
        fun dispatch -> transport.SubscribePackets matchId opponent (fun p -> dispatch (PacketReceived(matchId, p)))
      ]
    | _ -> []

  Subscriptions.tick (TimeSpan.FromMilliseconds(1000.0 / float ticksPerSecond)) Tick
  @ [
    [ "match" ], (fun dispatch -> transport.SubscribeMatch(MatchChanged >> dispatch))
  ]
  @ packets

// ─── View ─────────────────────────────────────────────────────────────────────

let private boxWidth = width * 3 + 2

let private line (color: Color) (text: string) =
  Text.line [ Text.styledSpan (Nullable(Style color)) text ]

let private secondsLeft (ticks: int) =
  (max 0 ticks + ticksPerSecond - 1) / ticksPerSecond

let private phaseMessage (state: State) (names: string list) =
  match state.Phase with
  | Countdown goAt -> Color.Yellow, sprintf "round %d · %d…" state.Round (secondsLeft (goAt - state.Tick))
  | Fight ->
    let seconds = secondsLeft (state.RoundEndsAt - state.Tick)
    Color.Grey62, sprintf "round %d · %d:%02d" state.Round (seconds / 60) (seconds % 60)
  | RoundOver(Winner i, _) -> Board.playerColors[i], sprintf "%s wins round %d" names[i] state.Round
  | RoundOver(Draw, _) -> Color.Grey62, sprintf "round %d is a draw" state.Round
  | MatchOver i -> Color.Gold1, sprintf "★ %s wins the match ★ · Enter: rematch" names[i]

let private gameLines (model: Model) (r: Running) =
  let user = model.Transport.User
  let names = [ r.Info.Host; r.Info.Guest ]
  let glyphs = Board.glyphs names
  let state = Netcode.display r.Net

  let messageColor, message =
    match r.Info.Status, r.Info.LeftBy with
    | MatchStatus.Abandoned, "" -> Color.Red1, "out of sync · match aborted · Enter: new match"
    | MatchStatus.Abandoned, left when left = user -> Color.Red1, "you left the match · Enter: new match"
    | MatchStatus.Abandoned, left -> Color.Green3, sprintf "%s left · you win · Enter: new match" left
    | _ when not (Netcode.canAdvance r.Net) && not (isDecided r) ->
      Color.Yellow, sprintf "waiting for %s…" (opponentOf user r.Info)
    | _ -> phaseMessage state names

  let hint =
    match model.ConfirmForfeit with
    | true -> line Color.Red1 " q again: forfeit · any other key: keep playing"
    | false -> line Color.Grey50 " ←↑↓→/wasd move · space bomb · q quit"

  [ Board.playerLine state names glyphs 0; Board.playerLine state names glyphs 1 ]
  @ [ line messageColor (" " + message) ]
  @ Board.rows state glyphs
  @ [ hint ]

let private lobbyLines (model: Model) =
  let user = model.Transport.User

  let text =
    match model.Loaded, model.Joining, model.Match with
    | false, _, _ -> [ Color.Grey62, "loading…" ]
    | _, Some _, _ -> [ Color.Grey62, "joining the match…" ]
    | _, _, Some info when info.Status = MatchStatus.Open && info.Host = user -> [
        Color.Yellow, "waiting for an opponent…"
        Color.Grey62, "others in this session: b, then Enter"
        Color.Grey50, "q: withdraw"
      ]
    | _, _, Some info when info.Status = MatchStatus.Open -> [
        Color.Yellow, sprintf "%s challenges you!" info.Host
        Color.Grey50, "Enter: accept · q: close"
      ]
    | _, _, Some info when info.Status = MatchStatus.Playing -> [
        Color.Grey62, sprintf "%s vs %s in progress" info.Host info.Guest
        Color.Grey50, "q: close"
      ]
    | _ -> [
        Color.Grey62, "no open challenge in this session"
        Color.Grey50, "Enter: challenge · q: close"
      ]

  let error =
    match model.Error with
    | Some e -> [ Color.Red1, e ]
    | None -> []

  [ Text.line [ Text.span "" ] ]
  @ [ for color, t in text @ error -> line color (" " + t) ]

let view (model: Model) _dispatch : IWidget =
  let title, lines =
    match model.Running with
    | Some r -> sprintf "tuiboom · %s vs %s" r.Info.Host r.Info.Guest, gameLines model r
    | None -> "tuiboom", lobbyLines model

  { new IWidget with
      member _.Render(ctx: RenderContext) =
        let boxed =
          box (Look.fromColor Color.OrangeRed1)
          |> withTitle title
          |> withInnerWidget (paragraph lines :> IWidget)
          :> IWidget

        ctx.Render(
          popup (min ctx.Viewport.Width boxWidth) (min ctx.Viewport.Height (lines.Length + 2))
          |> withPopupContent boxed
          :> IWidget
        )
  }
