module GlobalKeys

open System
open Keymap

type Msg =
  | StageDrive
  | FastForward
  | PauseResume

// `pauseHelp` is Some "pause"/"resume" when there's a running/paused drive to toggle,
// None when the action is unavailable (then it's disabled and hidden from the help bar).
let private bindings (stageHelp: string) (canFastForward: bool) (pauseHelp: string option) : KeyBinding<unit, Msg> list = [
  KeyBinding.create 's' stageHelp StageDrive
  KeyBinding.dynamic (CharKey 'f') (fun _ -> {
    Description = "fast-forward"
    Message =
      match canFastForward with
      | true -> Some FastForward
      | false -> None
  })
  KeyBinding.dynamic (CharKey 'p') (fun _ -> {
    Description = pauseHelp |> Option.defaultValue "pause"
    Message =
      match pauseHelp with
      | Some _ -> Some PauseResume
      | None -> None
  })
]

let handleKey (canFastForward: bool) (pauseHelp: string option) (key: ConsoleKeyInfo) : Msg option =
  KeyBinding.handleKey (bindings "" canFastForward pauseHelp) key ()

let keyMap (stageHelp: string) (canFastForward: bool) (pauseHelp: string option) : Spectre.Tui.App.IKeyMap =
  KeyBinding.toKeyMap (bindings stageHelp canFastForward pauseHelp) ()
