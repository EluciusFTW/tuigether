module Tuiboom.Board

open System
open Spectre.Console
open SpectreTuff
open Tuiboom.Game

let playerColors = [ Color.DeepSkyBlue1; Color.HotPink ]

let private floorBg = Color.Grey11
let private pillarBg = Color.Grey37
let private crateFg = Color.Orange4
let private crateBg = Color.Tan
let private flameBg = Color.OrangeRed1
let private powerUpBg = Color.Gold1

let private styled (fg: Color) (bg: Color) (text: string) =
  Text.styledSpan (Nullable(Style(Nullable fg, Nullable bg, Nullable Decoration.Bold))) text

/// One letter per player: their initial, or 1/2 when both share it.
let glyphs (names: string list) =
  let initials =
    names
    |> List.map (fun name ->
      match String.IsNullOrEmpty name with
      | true -> '?'
      | false -> Char.ToUpperInvariant name[0])

  match initials with
  | [ a; b ] when a = b -> [ '1'; '2' ]
  | _ -> initials

let private powerUpGlyph =
  function
  | ExtraBomb -> " b "
  | Range -> " f "
  | Speed -> " s "

let private cellSpan (state: State) (glyphs: char list) (pos: int * int) =
  let x, y = pos

  let alive =
    state.Players
    |> List.indexed
    |> List.filter (fun (_, p) -> p.Alive && p.X = x && p.Y = y)
    |> List.map fst

  let fallen = state.Players |> List.exists (fun p -> not p.Alive && p.X = x && p.Y = y)

  let bomb = bombAt state pos
  let burning = state.Flames.ContainsKey pos

  let groundBg =
    match burning, bomb with
    | true, _ -> flameBg
    | false, Some _ -> Color.Red3
    | false, None -> floorBg

  match alive with
  | [ i ] -> styled playerColors[i] groundBg (sprintf " %c " glyphs[i])
  | [ a; b ] -> styled Color.White groundBg (sprintf "%c %c" glyphs[a] glyphs[b])
  | _ ->
    match isPillar pos, state.Crates.Contains pos, burning, bomb, Map.tryFind pos state.PowerUps with
    | true, _, _, _, _ -> styled pillarBg pillarBg "   "
    | _, true, _, _, _ -> styled crateFg crateBg "▒▒▒"
    | _, _, true, _, _ -> styled Color.Yellow flameBg " * "
    | _, _, _, Some b, _ ->
      // The fuse flickers during its last second.
      let fg =
        match b.ExplodesAt - state.Tick <= ticksPerSecond && state.Tick % 2 = 0 with
        | true -> Color.Red1
        | false -> Color.White

      styled fg floorBg " ● "
    | _, _, _, _, Some powerUp -> styled Color.Black powerUpBg (powerUpGlyph powerUp)
    | _ when fallen -> styled Color.Grey50 floorBg " x "
    | _ -> styled floorBg floorBg "   "

let rows (state: State) (glyphs: char list) = [
  for y in 0 .. height - 1 -> Text.line [ for x in 0 .. width - 1 -> cellSpan state glyphs (x, y) ]
]

let private recordStars (wins: int) =
  String.replicate wins "★" + String.replicate (winsNeeded - wins) "☆"

/// "A caren      ●2 ✶3 »1   ★☆☆": a player's power-ups and won rounds.
let playerLine (state: State) (names: string list) (glyphs: char list) (index: int) =
  let player = state.Players[index]

  let color =
    match player.Alive with
    | true -> playerColors[index]
    | false -> Color.Grey50

  Text.line [
    Text.styledSpan
      (Nullable(Style(Nullable color, Nullable(), Nullable Decoration.Bold)))
      (sprintf " %c %-12s" glyphs[index] (names[index] |> Seq.truncate 12 |> String.Concat))
    Text.styledSpan
      (Nullable(Style Color.Grey62))
      (sprintf "●%d ✶%d »%d   " player.MaxBombs player.Range (speedLevel player))
    Text.styledSpan (Nullable(Style Color.Gold1)) (recordStars player.Wins)
  ]
