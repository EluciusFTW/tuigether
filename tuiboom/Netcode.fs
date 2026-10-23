module Tuiboom.Netcode

// Rollback netcode. Each client simulates the whole match itself (Game.step is
// deterministic) and only actions travel, tagged with the tick they apply at.
//
// - Base: the state up to which both players' inputs are known. Final; never rolls back.
// - Current: Base advanced to the local tick, assuming "no action" for opponent ticks
//   not heard yet. When their actions arrive, Current is re-simulated from Base.
//
// The local tick may run at most `maxAhead` ticks past the last tick the opponent
// confirmed. Both sides obey this, so they keep each other in step without a shared
// clock, and every late action is guaranteed to fall inside the re-simulated window.

open Tuiboom.Game

let maxAhead = 15

// Send a packet at least this often (in update ticks), even with no actions, so the
// opponent learns our progress and knows we're still there.
let private heartbeatTicks = 2
let private hashEvery = 50

type Packet = {
  Seq: int
  /// All of the sender's actions for ticks up to here are in this or earlier packets.
  UpTo: int
  Actions: (int * Action) list
  /// The sender's fingerprint of its Base at HashTick (-1: none yet).
  HashTick: int
  Hash: int
}

type Net = {
  Me: int
  Base: State
  Current: State
  /// (player, tick) -> that player's actions at that tick, in order.
  Inputs: Map<int * int, Action list>
  RemoteUpTo: int
  NextRemoteSeq: int
  /// Packets that arrived ahead of a missing one, held until the gap is filled.
  EarlyPackets: Map<int, Packet>
  NextSeq: int
  Unsent: (int * Action) list
  InFlight: Packet option
  AwaitingAck: bool
  SinceSend: int
  /// A move pressed during the move cooldown; it's sent once the cooldown has passed.
  PendingMove: Dir option
  LatestHash: int * int
  OwnHashes: Map<int, int>
  RemoteHashes: Map<int, int>
  Desync: int option
}

let private opponent (net: Net) =
  1 - net.Me

let private inputsAt (inputs: Map<int * int, Action list>) (tick: int) (player: int) =
  Map.tryFind (player, tick) inputs |> Option.defaultValue []

let private simulate (inputs: Map<int * int, Action list>) (toTick: int) (from: State) =
  let rec go (s: State) =
    match s.Tick < toTick with
    | true -> go (step (inputsAt inputs (s.Tick + 1)) s)
    | false -> s

  go from

let private addInput (player: int) (tick: int) (action: Action) (inputs: Map<int * int, Action list>) =
  inputs
  |> Map.change (player, tick) (fun existing -> Some((Option.defaultValue [] existing) @ [ action ]))

let private compareHashes (net: Net) =
  let common =
    net.OwnHashes
    |> Map.toList
    |> List.choose (fun (tick, own) -> Map.tryFind tick net.RemoteHashes |> Option.map (fun r -> tick, own, r))

  match common with
  | [] -> net
  | _ ->
    let latest = common |> List.map (fun (t, _, _) -> t) |> List.max
    let mismatch = common |> List.tryFind (fun (_, own, remote) -> own <> remote)

    {
      net with
          OwnHashes = net.OwnHashes |> Map.filter (fun t _ -> t > latest)
          RemoteHashes = net.RemoteHashes |> Map.filter (fun t _ -> t > latest)
          Desync =
            match net.Desync, mismatch with
            | None, Some(t, _, _) -> Some t
            | desync, _ -> desync
    }

/// Moves Base forward as far as both players' inputs are known.
let private advanceBase (net: Net) =
  let target = min net.RemoteUpTo net.Current.Tick

  let rec go (b: State) latest hashes =
    match b.Tick < target with
    | false -> b, latest, hashes
    | true ->
      let b = step (inputsAt net.Inputs (b.Tick + 1)) b

      match b.Tick % hashEvery with
      | 0 ->
        let h = hash b
        go b (b.Tick, h) (Map.add b.Tick h hashes)
      | _ -> go b latest hashes

  let b, latest, hashes = go net.Base net.LatestHash net.OwnHashes

  {
    net with
        Base = b
        LatestHash = latest
        OwnHashes = hashes
        // Nothing before Base is ever simulated again.
        Inputs = net.Inputs |> Map.filter (fun (_, t) _ -> t > b.Tick)
  }
  |> compareHashes

let private record (tick: int) (action: Action) (net: Net) = {
  net with
      Inputs = addInput net.Me tick action net.Inputs
      Unsent = net.Unsent @ [ tick, action ]
}

let private create (me: int) (seed: int) =
  let start = Game.create seed

  {
    Me = me
    Base = start
    Current = start
    Inputs = Map.empty
    RemoteUpTo = 0
    NextRemoteSeq = 0
    EarlyPackets = Map.empty
    NextSeq = 0
    Unsent = []
    InFlight = None
    AwaitingAck = false
    SinceSend = heartbeatTicks
    PendingMove = None
    LatestHash = -1, 0
    OwnHashes = Map.empty
    RemoteHashes = Map.empty
    Desync = None
  }

/// Starts (or, after a restart, resumes) a match. `ownPackets` are the packets this
/// player already sent in this match: their actions are replayed and the local tick
/// continues where it left off.
let start (me: int) (seed: int) (ownPackets: Packet list) =
  let net = create me seed

  match ownPackets with
  | [] -> net
  | packets ->
    let inputs =
      packets
      |> List.sortBy _.Seq
      |> List.collect _.Actions
      |> List.fold (fun inputs (tick, action) -> addInput me tick action inputs) net.Inputs

    let resumeTick = packets |> List.map _.UpTo |> List.max

    {
      net with
          Inputs = inputs
          Current = simulate inputs resumeTick net.Base
          NextSeq = (packets |> List.map _.Seq |> List.max) + 1
    }

let private canMoveAt (tick: int) (net: Net) =
  let player = net.Current.Players[net.Me]

  net.Current.Phase = Fight
  && player.Alive
  && player.NextMoveTick <= tick
  && inputsAt net.Inputs tick net.Me
     |> List.forall (function
       | Move _ -> false
       | PlaceBomb -> true)

let private flushPendingMove (net: Net) =
  let next = net.Current.Tick + 1

  match net.PendingMove with
  | Some dir when canMoveAt next net -> {
      record next (Move dir) net with
          PendingMove = None
    }
  | Some _ when net.Current.Phase <> Fight -> { net with PendingMove = None }
  | _ -> net

/// A local key press. It applies at the next tick, so the own player never waits on the network.
let act (action: Action) (net: Net) =
  match action with
  | Move dir -> flushPendingMove { net with PendingMove = Some dir }
  | PlaceBomb -> record (net.Current.Tick + 1) PlaceBomb net

/// False while we're `maxAhead` ticks past what the opponent has confirmed: the game
/// waits for them instead of predicting further.
let canAdvance (net: Net) =
  net.Current.Tick - net.RemoteUpTo < maxAhead

/// One local tick, if allowed. When the opponent is ahead, take two to catch up.
let advance (net: Net) =
  let once (net: Net) =
    match canAdvance net with
    | false -> net
    | true ->
      let net = flushPendingMove net

      {
        net with
            Current = step (inputsAt net.Inputs (net.Current.Tick + 1)) net.Current
      }
      |> advanceBase

  let net =
    once {
      net with
          SinceSend = net.SinceSend + 1
    }

  match net.RemoteUpTo > net.Current.Tick + 2 with
  | true -> once net
  | false -> net

let private applyRemote (packet: Packet) (net: Net) =
  let them = opponent net

  {
    net with
        Inputs =
          packet.Actions
          |> List.fold (fun inputs (tick, action) -> addInput them tick action inputs) net.Inputs
        RemoteUpTo = max net.RemoteUpTo packet.UpTo
        NextRemoteSeq = packet.Seq + 1
        RemoteHashes =
          match packet.HashTick with
          | t when t >= 0 -> Map.add t packet.Hash net.RemoteHashes
          | _ -> net.RemoteHashes
  }

/// An opponent packet. Packets are applied strictly in sequence; when one carries
/// actions for ticks already predicted, Current is rolled back and re-simulated.
let receive (packet: Packet) (net: Net) =
  match packet.Seq < net.NextRemoteSeq with
  | true -> net
  | false ->
    let rec drain (net: Net) (early: Map<int, Packet>) rollback =
      match Map.tryFind net.NextRemoteSeq early with
      | None -> { net with EarlyPackets = early }, rollback
      | Some p ->
        let late = p.Actions |> List.exists (fun (tick, _) -> tick <= net.Current.Tick)
        drain (applyRemote p net) (Map.remove p.Seq early) (rollback || late)

    let net, rollback = drain net (Map.add packet.Seq packet net.EarlyPackets) false
    let net = advanceBase net

    match rollback with
    | true -> {
        net with
            Current = simulate net.Inputs net.Current.Tick net.Base
      }
    | false -> net

/// The next packet to send, if one is due. Only one is in flight at a time, so they
/// reach the opponent in order; a failed one is sent again unchanged.
let takePacket (net: Net) : Net * Packet option =
  match net.InFlight, net.AwaitingAck with
  | Some _, true -> net, None
  | Some packet, false -> { net with AwaitingAck = true }, Some packet
  | None, _ ->
    match net.Unsent, net.SinceSend >= heartbeatTicks with
    | [], false -> net, None
    | actions, _ ->
      let hashTick, hashValue = net.LatestHash

      let packet = {
        Seq = net.NextSeq
        UpTo = net.Current.Tick
        Actions = actions
        HashTick = hashTick
        Hash = hashValue
      }

      {
        net with
            InFlight = Some packet
            AwaitingAck = true
            Unsent = []
            NextSeq = net.NextSeq + 1
            SinceSend = 0
      },
      Some packet

let sent (ok: bool) (net: Net) =
  match ok with
  | true -> {
      net with
          InFlight = None
          AwaitingAck = false
    }
  | false -> { net with AwaitingAck = false }

/// What to draw. Mostly the predicted Current, so own moves show instantly, but deaths,
/// scores and round results come from Base: nobody is shown dying and then coming back
/// after a correction.
let display (net: Net) =
  match net.Base.Round = net.Current.Round with
  | false -> net.Base
  | true -> {
      net.Current with
          Phase = net.Base.Phase
          Players =
            List.map2
              (fun (c: Player) (b: Player) -> {
                c with
                    Alive = b.Alive
                    Wins = b.Wins
              })
              net.Current.Players
              net.Base.Players
    }
