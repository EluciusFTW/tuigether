namespace Tuiboom

open System
open Tuiboom.Game

/// The current match of a session, as stored by the host app.
[<CLIMutable>]
type MatchInfo = {
  MatchId: string
  /// Player 0; created the match.
  Host: string
  /// Player 1; empty while the match is open.
  Guest: string
  Seed: int
  Status: string
  Winner: string
  LeftBy: string
}

// Literals, so they can be matched on.
module MatchStatus =
  [<Literal>]
  let Open = "Open"

  [<Literal>]
  let Playing = "Playing"

  [<Literal>]
  let Over = "Over"

  [<Literal>]
  let Abandoned = "Abandoned"

/// A Netcode.Packet in a shape any JSON store can hold: the actions are packed into a
/// string like "12:U;12:B;14:L".
[<CLIMutable>]
type WirePacket = {
  Seq: int
  UpTo: int
  Actions: string
  HashTick: int
  Hash: int
}

module Wire =
  let private actionCode =
    function
    | Move Up -> "U"
    | Move Down -> "D"
    | Move Left -> "L"
    | Move Right -> "R"
    | PlaceBomb -> "B"

  let private parseAction =
    function
    | "U" -> Some(Move Up)
    | "D" -> Some(Move Down)
    | "L" -> Some(Move Left)
    | "R" -> Some(Move Right)
    | "B" -> Some PlaceBomb
    | _ -> None

  let encode (packet: Netcode.Packet) : WirePacket = {
    Seq = packet.Seq
    UpTo = packet.UpTo
    Actions =
      packet.Actions
      |> List.map (fun (tick, action) -> sprintf "%d:%s" tick (actionCode action))
      |> String.concat ";"
    HashTick = packet.HashTick
    Hash = packet.Hash
  }

  let decode (wire: WirePacket) : Netcode.Packet = {
    Seq = wire.Seq
    UpTo = wire.UpTo
    Actions =
      match String.IsNullOrEmpty wire.Actions with
      | true -> []
      | false ->
        wire.Actions.Split ';'
        |> Array.toList
        |> List.choose (fun entry ->
          match entry.Split ':' with
          | [| tick; code |] -> parseAction code |> Option.map (fun action -> int tick, action)
          | _ -> None)
    HashTick = wire.HashTick
    Hash = wire.Hash
  }

/// How Tuiboom reaches the other player. Implemented by the host app (Tuigether: Firebase).
type Transport = {
  User: string
  SaveMatch: MatchInfo -> Async<Result<unit, string>>
  /// Removes the packets of earlier matches; called before a new match is created.
  ClearPackets: unit -> Async<unit>
  SendPacket: string -> string -> WirePacket -> Async<Result<unit, string>>
  /// All packets a player has sent in a match (matchId, user).
  LoadPackets: string -> string -> Async<WirePacket list>
  /// Fires with the current match on subscribe and on every change.
  SubscribeMatch: (MatchInfo option -> unit) -> IDisposable
  /// Fires for each packet a player sends in a match (matchId, user), including those
  /// already sent when subscribing.
  SubscribePackets: string -> string -> (WirePacket -> unit) -> IDisposable
}
