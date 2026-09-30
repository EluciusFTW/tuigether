module JourneyKeys

open System
open Keymap

type Msg =
  | StageDrive
  | FastForward
  | NextDrive
  | ToggleDriveLog

// `canNext` is true while a drive is stopped; otherwise `n` is disabled, hidden from
// the help bar, and falls through to the focused panel.
let private bindings
  (stageHelp: string)
  (canFastForward: bool)
  (canNext: bool)
  : KeyBinding<unit, Msg> list =
  [
    KeyBinding.create 's' stageHelp StageDrive
    KeyBinding.dynamic (CharKey 'f') (fun _ -> {
      Description = "fast-forward"
      Message =
        match canFastForward with
        | true -> Some FastForward
        | false -> None
    })
    KeyBinding.dynamic (CharKey 'n') (fun _ -> {
      Description = "next drive"
      Message =
        match canNext with
        | true -> Some NextDrive
        | false -> None
    })
    KeyBinding.create 'j' "journey log" ToggleDriveLog
  ]

let handleKey (canFastForward: bool) (canNext: bool) (key: ConsoleKeyInfo) : Msg option =
  KeyBinding.handleKey (bindings "" canFastForward canNext) key ()

let keyMap (stageHelp: string) (canFastForward: bool) (canNext: bool) : Spectre.Tui.App.IKeyMap =
  KeyBinding.toKeyMap (bindings stageHelp canFastForward canNext) ()
