module TextEditing

open System
open Spectre.Tui

// A single text-editing intent, decoded from a key and applied to a
// Spectre.Tui.TextBoxWidget. Shared by every component that edits text through a
// persisted editor widget, so the key bindings and dispatch live in one place.
type EditAction =
  | Insert of char
  | Backspace
  | DeleteForward
  | NewLine
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown
  | MoveHome
  | MoveEnd

// Decode a key into an edit action. `multiLine` enables Up/Down and Enter→NewLine;
// single-line callers own Enter (confirm) and get None for it here. Escape is
// never decoded here — callers always own it (exit/cancel).
let keyToAction (multiLine: bool) (key: ConsoleKeyInfo) : EditAction option =
  match key.Key with
  | ConsoleKey.LeftArrow -> Some MoveLeft
  | ConsoleKey.RightArrow -> Some MoveRight
  | ConsoleKey.Home -> Some MoveHome
  | ConsoleKey.End -> Some MoveEnd
  | ConsoleKey.Backspace -> Some Backspace
  | ConsoleKey.Delete -> Some DeleteForward
  | ConsoleKey.UpArrow when multiLine -> Some MoveUp
  | ConsoleKey.DownArrow when multiLine -> Some MoveDown
  | ConsoleKey.Enter when multiLine -> Some NewLine
  | _ when not (Char.IsControl key.KeyChar) -> Some(Insert key.KeyChar)
  | _ -> None

// Apply an action to the widget, mutating its caret/buffer in place.
let apply (action: EditAction) (editor: TextBoxWidget) : unit =
  match action with
  | Insert c -> editor.Insert(string c)
  | Backspace -> editor.DeleteBackward()
  | DeleteForward -> editor.DeleteForward()
  | NewLine -> editor.InsertNewLine()
  | MoveLeft -> editor.MoveLeft()
  | MoveRight -> editor.MoveRight()
  | MoveUp -> editor.MoveUp()
  | MoveDown -> editor.MoveDown()
  | MoveHome -> editor.MoveHome()
  | MoveEnd -> editor.MoveEnd()

// True for actions that change the text (vs pure caret movement) — lets callers
// decide whether a save is needed.
let isMutation (action: EditAction) : bool =
  match action with
  | Insert _
  | Backspace
  | DeleteForward
  | NewLine -> true
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown
  | MoveHome
  | MoveEnd -> false
