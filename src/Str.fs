module Str

// Drop the last character of a string, returning "" for an empty string. Used by
// every text-input handler's backspace path (goal, notes, todo, session title…).
let dropLast (s: string) =
  match s with
  | "" -> ""
  | _ -> s.[.. s.Length - 2]

// Greedy word wrap to `width` columns: words are kept whole where they fit, and
// a single word too long for a whole line is hard-broken rather than truncated,
// so no text is ever lost. Newlines in the input start a new line.
let wrap (width: int) (text: string) : string list =
  match width < 1 with
  | true -> [ text ]
  | false ->
    let hardBreak (word: string) =
      word |> Seq.chunkBySize width |> Seq.map System.String |> Seq.toList

    let words (line: string) =
      line.Split([| ' '; '\t' |], System.StringSplitOptions.RemoveEmptyEntries)
      |> Array.toList
      |> List.collect (fun word ->
        match word.Length <= width with
        | true -> [ word ]
        | false -> hardBreak word)

    let fill (filled, current: string) (word: string) =
      match current with
      | "" -> filled, word
      | _ when current.Length + 1 + word.Length <= width -> filled, current + " " + word
      | _ -> current :: filled, word

    let wrapLine (line: string) =
      match line |> words |> List.fold fill ([], "") with
      | [], "" -> [ "" ]
      | filled, current -> List.rev (current :: filled)

    text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
    |> Array.toList
    |> List.collect wrapLine

// Wraps `text` under a list marker the way markdown list views render: the
// marker prefixes the first line and continuation lines are indented to line up
// with the text after it, so a wrapped item still reads as one block.
let wrapHanging (marker: string) (width: int) (text: string) : string list =
  let indent = System.String(' ', marker.Length)

  wrap (width - marker.Length) text
  |> List.mapi (fun index line ->
    match index with
    | 0 -> marker + line
    | _ -> indent + line)

// Caps a wrapped block at `height` lines, marking the cut with an ellipsis that
// stays within `width`. A list row taller than its panel is dropped from the
// layout entirely, so capping keeps an over-long item visible (if abbreviated)
// instead of blanking the panel.
let capLines (height: int) (width: int) (lines: string list) : string list =
  match height > 0 && List.length lines > height with
  | false -> lines
  | true ->
    let kept = lines |> List.truncate height

    let elided =
      match List.last kept with
      | last when last.Length < width -> last + "…"
      | last -> last.[.. width - 2] + "…"

    kept
    |> List.mapi (fun index line ->
      match index = height - 1 with
      | true -> elided
      | false -> line)
