module Tuiboom.Game

// The pure, deterministic rules. Both clients run this same simulation from the same
// seed and inputs (see Netcode), so it must produce identical states step for step:
// integers only, its own RNG instead of System.Random, and a fixed player order.

let width = 15
let height = 13

/// Simulation steps per second.
let ticksPerSecond = 10

let winsNeeded = 3

let private fuseTicks = 30
let private flameTicks = 5
let private baseMoveCooldown = 3
let private minMoveCooldown = 2
let private maxBombs = 8
let private maxRange = 8
let private countdownTicks = 30
let private roundOverTicks = 40
let private roundTimeLimit = 180 * ticksPerSecond

type Dir =
  | Up
  | Down
  | Left
  | Right

type Action =
  | Move of Dir
  | PlaceBomb

type PowerUp =
  | ExtraBomb
  | Range
  | Speed

type Player = {
  X: int
  Y: int
  Alive: bool
  MaxBombs: int
  Range: int
  MoveCooldown: int
  NextMoveTick: int
  Wins: int
}

type Bomb = {
  X: int
  Y: int
  Owner: int
  Range: int
  ExplodesAt: int
}

type Outcome =
  | Winner of int
  | Draw

type Phase =
  | Countdown of goAt: int
  | Fight
  | RoundOver of Outcome * nextRoundAt: int
  | MatchOver of winner: int

type State = {
  Seed: int
  Round: int
  Tick: int
  RoundEndsAt: int
  Phase: Phase
  Crates: Set<int * int>
  /// Power-ups still hidden under a crate; revealed when the crate is blown up.
  Hidden: Map<int * int, PowerUp>
  PowerUps: Map<int * int, PowerUp>
  Players: Player list
  Bombs: Bomb list
  /// Burning tiles, mapped to the tick their flame dies out.
  Flames: Map<int * int, int>
}

let isPillar (x: int, y: int) =
  x <= 0
  || y <= 0
  || x >= width - 1
  || y >= height - 1
  || (x % 2 = 0 && y % 2 = 0)

let spawns = [ 1, 1; width - 2, height - 2 ]

// Each spawn keeps an L of free tiles, so a player can always place a bomb and step away.
let private safeZone =
  set [
    1, 1
    2, 1
    1, 2
    width - 2, height - 2
    width - 3, height - 2
    width - 2, height - 3
  ]

// xorshift32: tiny, and identical on every machine and .NET version.
let private nextRandom (s: uint32) =
  let s = s ^^^ (s <<< 13)
  let s = s ^^^ (s >>> 17)
  s ^^^ (s <<< 5)

let private roundSeed (seed: int) (round: int) =
  (uint32 seed * 2654435761u + uint32 round * 40503u) ||| 1u

let private hiddenPowerUp (roll: uint32) =
  match roll % 100u with
  | r when r < 10u -> Some ExtraBomb
  | r when r < 20u -> Some Range
  | r when r < 25u -> Some Speed
  | _ -> None

let private layout (seed: uint32) =
  let freeTiles = [
    for y in 1 .. height - 2 do
      for x in 1 .. width - 2 -> x, y
  ]

  let _, crates, hidden =
    freeTiles
    |> List.filter (fun pos -> not (isPillar pos) && not (safeZone.Contains pos))
    |> List.fold
      (fun (rng, crates, hidden) pos ->
        let rng = nextRandom rng

        match rng % 100u < 70u with
        | false -> rng, crates, hidden
        | true ->
          let rng = nextRandom rng

          let hidden =
            match hiddenPowerUp rng with
            | Some p -> Map.add pos p hidden
            | None -> hidden

          rng, Set.add pos crates, hidden)
      (seed, Set.empty, Map.empty)

  crates, hidden

let private startRound (seed: int) (round: int) (tick: int) (wins: int list) =
  let crates, hidden = layout (roundSeed seed round)
  let goAt = tick + countdownTicks

  {
    Seed = seed
    Round = round
    Tick = tick
    RoundEndsAt = goAt + roundTimeLimit
    Phase = Countdown goAt
    Crates = crates
    Hidden = hidden
    PowerUps = Map.empty
    Players =
      List.map2
        (fun (x, y) w -> {
          X = x
          Y = y
          Alive = true
          MaxBombs = 1
          Range = 2
          MoveCooldown = baseMoveCooldown
          NextMoveTick = 0
          Wins = w
        })
        spawns
        wins
    Bombs = []
    Flames = Map.empty
  }

let create (seed: int) =
  startRound seed 1 0 [ 0; 0 ]

/// How many speed power-ups have had an effect (0 = base speed).
let speedLevel (player: Player) =
  baseMoveCooldown - player.MoveCooldown

let bombAt (state: State) (x: int, y: int) =
  state.Bombs |> List.tryFind (fun b -> b.X = x && b.Y = y)

let private step1 (x: int, y: int) =
  function
  | Up -> x, y - 1
  | Down -> x, y + 1
  | Left -> x - 1, y
  | Right -> x + 1, y

let private isWalkable (state: State) (pos: int * int) =
  not (isPillar pos)
  && not (state.Crates.Contains pos)
  && (bombAt state pos).IsNone

let private collect (powerUp: PowerUp) (player: Player) =
  match powerUp with
  | ExtraBomb -> {
      player with
          MaxBombs = min maxBombs (player.MaxBombs + 1)
    }
  | Range -> {
      player with
          Range = min maxRange (player.Range + 1)
    }
  | Speed -> {
      player with
          MoveCooldown = max minMoveCooldown (player.MoveCooldown - 1)
    }

let private replacePlayer (index: int) (player: Player) (state: State) = {
  state with
      Players = state.Players |> List.updateAt index player
}

let private applyAction (state: State) (index: int) (action: Action) =
  let player = state.Players[index]

  match player.Alive, action with
  | false, _ -> state
  | true, Move dir when state.Tick >= player.NextMoveTick ->
    let target = step1 (player.X, player.Y) dir

    match isWalkable state target with
    | false -> state
    | true ->
      let moved = {
        player with
            X = fst target
            Y = snd target
            NextMoveTick = state.Tick + player.MoveCooldown
      }

      match Map.tryFind target state.PowerUps with
      | Some powerUp ->
        {
          state with
              PowerUps = Map.remove target state.PowerUps
        }
        |> replacePlayer index (collect powerUp moved)
      | None -> replacePlayer index moved state
  | true, Move _ -> state
  | true, PlaceBomb ->
    let active = state.Bombs |> List.filter (fun b -> b.Owner = index) |> List.length

    match active < player.MaxBombs && (bombAt state (player.X, player.Y)).IsNone with
    | true -> {
        state with
            Bombs =
              state.Bombs
              @ [
                {
                  X = player.X
                  Y = player.Y
                  Owner = index
                  Range = player.Range
                  ExplodesAt = state.Tick + fuseTicks
                }
              ]
      }
    | false -> state

/// The tiles one bomb's cross-shaped blast reaches. Each arm stops at a pillar, and at
/// the first crate or bomb it hits (that tile still burns). Returns the burning tiles,
/// the crates hit and the bombs hit (which detonate in the same tick).
let private blast (crates: Set<int * int>) (bombs: Bomb list) (bomb: Bomb) =
  let arm dir =
    let rec go pos remaining acc =
      let pos = step1 pos dir

      match remaining with
      | 0 -> acc
      | _ when isPillar pos -> acc
      | _ when crates.Contains pos ->
        let tiles, hitCrates, hitBombs = acc
        pos :: tiles, pos :: hitCrates, hitBombs
      | _ ->
        let tiles, hitCrates, hitBombs = acc

        match bombs |> List.tryFind (fun b -> b.X = fst pos && b.Y = snd pos) with
        | Some hit -> pos :: tiles, hitCrates, hit :: hitBombs
        | None -> go pos (remaining - 1) (pos :: tiles, hitCrates, hitBombs)

    go (bomb.X, bomb.Y) bomb.Range ([], [], [])

  [ Up; Down; Left; Right ]
  |> List.map arm
  |> List.fold
    (fun (tiles, hitCrates, hitBombs) (t, c, b) -> tiles @ t, hitCrates @ c, hitBombs @ b)
    ([ bomb.X, bomb.Y ], [], [])

let private detonate (state: State) =
  let flames = state.Flames |> Map.filter (fun _ until -> until > state.Tick)

  let triggered, waiting =
    state.Bombs
    |> List.partition (fun b -> b.ExplodesAt <= state.Tick || flames.ContainsKey(b.X, b.Y))

  // Chain reactions: every bomb a blast reaches joins the queue and explodes this tick.
  let rec chain queue waiting (tiles: Set<int * int>) (hitCrates: Set<int * int>) =
    match queue with
    | [] -> waiting, tiles, hitCrates
    | bomb :: rest ->
      let t, c, hitBombs = blast state.Crates waiting bomb
      let waiting = waiting |> List.filter (fun b -> not (List.contains b hitBombs))
      chain (rest @ hitBombs) waiting (tiles + Set.ofList t) (hitCrates + Set.ofList c)

  let remaining, tiles, hitCrates = chain triggered waiting Set.empty Set.empty

  // Power-ups lying in the open burn; those just revealed from a crate survive the blast.
  let revealed = state.Hidden |> Map.filter (fun pos _ -> hitCrates.Contains pos)

  let powerUps =
    revealed
    |> Map.fold
      (fun acc pos p -> Map.add pos p acc)
      (state.PowerUps |> Map.filter (fun pos _ -> not (tiles.Contains pos)))

  {
    state with
        Bombs = remaining
        Crates = state.Crates - hitCrates
        Hidden = state.Hidden |> Map.filter (fun pos _ -> not (hitCrates.Contains pos))
        PowerUps = powerUps
        Flames =
          tiles
          |> Set.fold (fun acc pos -> Map.add pos (state.Tick + flameTicks) acc) flames
  }

let private burnPlayers (state: State) = {
  state with
      Players =
        state.Players
        |> List.map (fun p ->
          match p.Alive && state.Flames.ContainsKey(p.X, p.Y) with
          | true -> { p with Alive = false }
          | false -> p)
}

let private resolveRound (state: State) =
  let alive =
    state.Players
    |> List.indexed
    |> List.filter (fun (_, p) -> p.Alive)
    |> List.map fst

  let finish outcome =
    match outcome with
    | Winner index ->
      let winner = state.Players[index]
      let winner = { winner with Wins = winner.Wins + 1 }
      let state = replacePlayer index winner state

      match winner.Wins >= winsNeeded with
      | true -> { state with Phase = MatchOver index }
      | false -> {
          state with
              Phase = RoundOver(outcome, state.Tick + roundOverTicks)
        }
    | Draw -> {
        state with
            Phase = RoundOver(outcome, state.Tick + roundOverTicks)
      }

  match alive with
  | [ index ] -> finish (Winner index)
  | [] -> finish Draw
  | _ when state.Tick >= state.RoundEndsAt -> finish Draw
  | _ -> state

/// Advances the simulation by one tick. `inputs player` lists that player's actions for
/// the new tick, applied in order; player 0 always acts before player 1.
let step (inputs: int -> Action list) (state: State) : State =
  let tick = state.Tick + 1
  let state = { state with Tick = tick }

  match state.Phase with
  | MatchOver _ -> state
  | Countdown goAt when tick >= goAt -> { state with Phase = Fight }
  | Countdown _ -> state
  | RoundOver(_, nextRoundAt) when tick >= nextRoundAt ->
    startRound state.Seed (state.Round + 1) tick (state.Players |> List.map _.Wins)
  | RoundOver _ -> state
  | Fight ->
    let acted =
      state.Players
      |> List.indexed
      |> List.fold (fun s (index, _) -> inputs index |> List.fold (fun s a -> applyAction s index a) s) state

    acted |> detonate |> burnPlayers |> resolveRound

/// A fingerprint of the whole state, used to detect when two clients' simulations have
/// diverged. Hand-rolled because string and record hash codes differ between processes.
let hash (state: State) =
  let mix h v =
    h * 31 + v

  let mixPos h (x, y) =
    mix (mix h x) y

  let phaseCode =
    match state.Phase with
    | Countdown goAt -> mix 1 goAt
    | Fight -> 2
    | RoundOver(Winner i, at) -> mix (mix 3 i) at
    | RoundOver(Draw, at) -> mix 4 at
    | MatchOver i -> mix 5 i

  let powerUpCode =
    function
    | ExtraBomb -> 1
    | Range -> 2
    | Speed -> 3

  let h = mix (mix (mix (mix 17 state.Tick) state.Round) state.RoundEndsAt) phaseCode
  let h = state.Crates |> Set.fold mixPos h
  let h = state.Hidden |> Map.fold (fun h pos p -> mix (mixPos h pos) (powerUpCode p)) h
  let h = state.PowerUps |> Map.fold (fun h pos p -> mix (mixPos h pos) (powerUpCode p)) h
  let h = state.Flames |> Map.fold (fun h pos until -> mix (mixPos h pos) until) h

  let h =
    state.Bombs
    |> List.fold (fun h b -> mix (mix (mix (mixPos h (b.X, b.Y)) b.Owner) b.Range) b.ExplodesAt) h

  state.Players
  |> List.fold
    (fun h p ->
      [
        p.X
        p.Y
        (if p.Alive then 1 else 0)
        p.MaxBombs
        p.Range
        p.MoveCooldown
        p.NextMoveTick
        p.Wins
      ]
      |> List.fold mix h)
    h
