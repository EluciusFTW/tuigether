module Clock

open System

// Single source of truth for the Unix-millisecond timestamps used throughout the
// app (session/timer/lock instants, drive events). Everything that needs "now"
// goes through here so the wire format stays consistent in one place.
let nowMs () : int64 =
  DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
