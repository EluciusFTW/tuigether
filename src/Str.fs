module Str

// Drop the last character of a string, returning "" for an empty string. Used by
// every text-input handler's backspace path (goal, notes, todo, session title…).
let dropLast (s: string) =
  match s with
  | "" -> ""
  | _ -> s.[.. s.Length - 2]
