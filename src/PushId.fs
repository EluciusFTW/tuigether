// Port of Firebase's client-side push-ID algorithm: 20-character lexicographically
// sortable IDs (8 chars timestamp + 12 chars randomness). Concurrent generation
// across users yields distinct keys, so per-item writes never collide.
module PushId

open System

let private chars = "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz"

let private rng = Random()
let private syncLock = obj ()
let private lastRandChars: int array = Array.zeroCreate 12
let mutable private lastPushTime = 0L

let generate () =
  lock syncLock (fun () ->
    let timestamp = Clock.nowMs ()
    let duplicateTime = timestamp = lastPushTime
    lastPushTime <- timestamp

    let timestampChars = Array.zeroCreate<char> 8
    let mutable remaining = timestamp

    for i in 7..-1..0 do
      timestampChars.[i] <- chars.[int (remaining % 64L)]
      remaining <- remaining / 64L

    match duplicateTime with
    | false ->
      for i in 0..11 do
        lastRandChars.[i] <- rng.Next(64)
    | true ->
      let mutable carryIndex = 11

      while carryIndex >= 0 && lastRandChars.[carryIndex] = 63 do
        lastRandChars.[carryIndex] <- 0
        carryIndex <- carryIndex - 1

      lastRandChars.[carryIndex] <- lastRandChars.[carryIndex] + 1

    let randChars = lastRandChars |> Array.map (fun n -> chars.[n])
    String(timestampChars) + String(randChars))
