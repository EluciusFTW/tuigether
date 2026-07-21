module Clipboard

open System
open System.Runtime.InteropServices

// Linux: order by display server. The wrong-server tool exits non-zero (no
// throw), so it is the last resort. Each entry is (fileName, arguments).
let private copyCandidates () : (string * string) list =
  match RuntimeInformation.IsOSPlatform OSPlatform.Windows, RuntimeInformation.IsOSPlatform OSPlatform.OSX with
  | true, _ -> [ "clip", "" ]
  | _, true -> [ "pbcopy", "" ]
  | _ ->
    let xclip = "xclip", "-selection clipboard"
    let xsel = "xsel", "--clipboard --input"
    let wlCopy = "wl-copy", ""

    match Environment.GetEnvironmentVariable "WAYLAND_DISPLAY" with
    | null
    | "" -> [ xclip; xsel; wlCopy ]
    | _ -> [ wlCopy; xclip; xsel ]

// Read counterparts, same display-server ordering. `clip` is write-only, so
// Windows reads via PowerShell; -Raw preserves newlines. wl-paste's
// --no-newline drops the trailing newline it would otherwise append.
let private readCandidates () : (string * string) list =
  match RuntimeInformation.IsOSPlatform OSPlatform.Windows, RuntimeInformation.IsOSPlatform OSPlatform.OSX with
  | true, _ -> [ "powershell", "-NoProfile -Command \"Get-Clipboard -Raw\"" ]
  | _, true -> [ "pbpaste", "" ]
  | _ ->
    let xclip = "xclip", "-selection clipboard -o"
    let xsel = "xsel", "--clipboard --output"
    let wlPaste = "wl-paste", "--no-newline"

    match Environment.GetEnvironmentVariable "WAYLAND_DISPLAY" with
    | null
    | "" -> [ xclip; xsel; wlPaste ]
    | _ -> [ wlPaste; xclip; xsel ]

let private tryCopyWith (fileName: string) (arguments: string) (text: string) : bool =
  try
    let psi = Diagnostics.ProcessStartInfo()
    psi.FileName <- fileName
    psi.Arguments <- arguments
    psi.UseShellExecute <- false
    psi.RedirectStandardInput <- true

    use proc = Diagnostics.Process.Start psi
    proc.StandardInput.Write text
    proc.StandardInput.Close()
    proc.WaitForExit()
    proc.ExitCode = 0
  with _ ->
    false

let private tryReadWith (fileName: string) (arguments: string) : string option =
  try
    let psi = Diagnostics.ProcessStartInfo()
    psi.FileName <- fileName
    psi.Arguments <- arguments
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true

    use proc = Diagnostics.Process.Start psi
    let output = proc.StandardOutput.ReadToEnd()
    proc.WaitForExit()

    match proc.ExitCode = 0 with
    | true -> Some output
    | false -> None
  with _ ->
    None

// Copy text to the OS clipboard. Ok carries the tool that succeeded.
let copy (text: string) : Result<string, string> =
  let rec attempt remaining =
    match remaining with
    | [] -> Error "no working clipboard tool found (install xclip, xsel, or wl-copy)"
    | (fileName, arguments) :: rest ->
      match tryCopyWith fileName arguments text with
      | true -> Ok fileName
      | false -> attempt rest

  attempt (copyCandidates ())

// Read text from the OS clipboard.
let read () : Result<string, string> =
  let rec attempt remaining =
    match remaining with
    | [] -> Error "no working clipboard tool found (install xclip, xsel, or wl-paste)"
    | (fileName, arguments) :: rest ->
      match tryReadWith fileName arguments with
      | Some text -> Ok text
      | None -> attempt rest

  attempt (readCandidates ())
