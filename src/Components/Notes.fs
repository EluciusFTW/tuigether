module Notes

open System
open Elmish
open Firebase.Database
open Spectre.Tui
open Keymap
open Locking
open SpectreTuff
open SpectreTuff.Widgets

type InputMode =
  | Normal
  | Insert

type Model = {
  InputMode: InputMode
  FreetextContent: string
  FreetextSaveToken: int
  InsertActivityToken: int
  Lock: Locking.Lock option
  User: string
  Persistence: Firebase.Persistence
  // Live editor, present only while in Insert mode. It owns the caret and does
  // all at-cursor editing; FreetextContent is kept in sync from it on each edit
  // and remains the source of truth for persistence and Normal-mode rendering.
  Editor: TextBoxWidget option
}

type Msg =
  | EnterInsert
  | ExitInsert
  | TypeChar of char
  | TypeBackspace
  | TypeDelete
  | TypeNewLine
  | CaretLeft
  | CaretRight
  | CaretUp
  | CaretDown
  | CaretHome
  | CaretEnd
  | MaybeSaveFreetext of int
  | MaybeAutoExitInsert of int
  | RemoteStateLoaded of Session.NotesState option
  | StateSaved

let private freetextDebounceMs = 300
let private autoExitInsertMs = 30_000

let private isLockedByOther (model: Model) =
  Locking.heldByOther (Clock.nowMs ()) model.User model.Lock

let isHoldingLock (model: Model) =
  match model.InputMode, model.Lock with
  | Insert, Some l -> l.Owner = model.User
  | _ -> false

let private insertModeBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Escape "exit insert" ExitInsert
]

let private normalBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.dynamic (CharKey 'i') (fun model ->
    match isLockedByOther model with
    | true ->
      let owner =
        model.Lock
        |> Option.map (fun l -> l.Owner)
        |> Option.defaultValue "another user"

      {
        Description = sprintf "locked by %s" owner
        Message = Some EnterInsert
      }
    | false -> {
        Description = "insert"
        Message = Some EnterInsert
      })
]

let handleKey (key: ConsoleKeyInfo) (model: Model) : Msg option =
  match model.InputMode with
  | Insert ->
    match key.Key with
    | ConsoleKey.Escape -> Some ExitInsert
    | ConsoleKey.LeftArrow -> Some CaretLeft
    | ConsoleKey.RightArrow -> Some CaretRight
    | ConsoleKey.UpArrow -> Some CaretUp
    | ConsoleKey.DownArrow -> Some CaretDown
    | ConsoleKey.Home -> Some CaretHome
    | ConsoleKey.End -> Some CaretEnd
    | ConsoleKey.Backspace -> Some TypeBackspace
    | ConsoleKey.Delete -> Some TypeDelete
    | ConsoleKey.Enter -> Some TypeNewLine
    // Only insert genuinely printable characters. This drops control keys such
    // as Tab ('\t'), whose literal insertion garbled the editor, along with the
    // null char produced by unhandled navigation keys.
    | _ when not (Char.IsControl key.KeyChar) -> Some(TypeChar key.KeyChar)
    | _ -> None
  | Normal ->
    match key.KeyChar with
    | 'i' -> Some EnterInsert
    | _ -> None

let capturesInput (model: Model) =
  match model.InputMode with
  | Insert -> true
  | Normal -> false

let keyMap (model: Model) =
  let bindings =
    match model.InputMode with
    | Insert -> insertModeBindings
    | Normal -> normalBindings

  KeyBinding.toKeyMap bindings model

let init (client: FirebaseClient) (sessionId: string) (user: string) = {
  InputMode = Normal
  FreetextContent = ""
  FreetextSaveToken = 0
  InsertActivityToken = 0
  Lock = None
  User = user
  Persistence = {
    Client = client
    SessionId = sessionId
  }
  Editor = None
}

let private saveFreetextCmd (model: Model) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> Firebase.Notes.saveFreetext model.Persistence.Client model.Persistence.SessionId model.FreetextContent)
    ()
    (fun () -> StateSaved)

// Debounce freetext writes: each typed character bumps a token and schedules a
// MaybeSaveFreetext for that token after a short idle delay. The actual save
// only fires if the token still matches the latest one — so a burst of fast
// keystrokes collapses into a single Firebase write at the end.
let private scheduleFreetextSave (token: int) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep freetextDebounceMs }) () (fun () -> MaybeSaveFreetext token)

// Auto-exit Insert mode after autoExitInsertMs of no typing activity. Every
// keystroke bumps the activity token and schedules a fresh check; only the
// scheduled check whose token still matches actually exits.
let private scheduleAutoExit (token: int) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep autoExitInsertMs }) () (fun () -> MaybeAutoExitInsert token)

let private saveLockCmd (model: Model) : Cmd<Msg> =
  match model.Lock with
  | Some lock ->
    Cmd.OfAsync.perform
      (fun () -> Firebase.Notes.saveLock model.Persistence.Client model.Persistence.SessionId lock.Owner lock.LockedAt)
      ()
      (fun () -> StateSaved)
  | None -> []

let private releaseLockCmd (model: Model) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> Firebase.Notes.releaseLock model.Persistence.Client model.Persistence.SessionId)
    ()
    (fun () -> StateSaved)

// Apply an at-caret edit to the live editor, sync the content back for
// persistence, and schedule the debounced save plus the idle auto-exit.
let private editWith (mutate: TextBoxWidget -> unit) (model: Model) : Model * Cmd<Msg> =
  match model.Editor with
  | None -> model, []
  | Some editor ->
    mutate editor
    let bumped = model.FreetextSaveToken + 1
    let activityToken = model.InsertActivityToken + 1

    let updated = {
      model with
          FreetextContent = editor.Text
          FreetextSaveToken = bumped
          InsertActivityToken = activityToken
    }

    updated, Cmd.batch [ scheduleFreetextSave bumped; scheduleAutoExit activityToken ]

// Move the caret. Content is unchanged (no save), but it counts as activity so
// the idle auto-exit timer is refreshed.
let private moveCaret (mutate: TextBoxWidget -> unit) (model: Model) : Model * Cmd<Msg> =
  match model.Editor with
  | None -> model, []
  | Some editor ->
    mutate editor
    let activityToken = model.InsertActivityToken + 1
    { model with InsertActivityToken = activityToken }, scheduleAutoExit activityToken

let update msg model =
  match msg with
  | EnterInsert ->
    match isLockedByOther model with
    | true -> model, []
    | false ->
      let activityToken = model.InsertActivityToken + 1

      let lock = {
        Owner = model.User
        LockedAt = Clock.nowMs ()
      }

      // Build the live editor from the current content and place the caret at
      // the end (matching the previous behaviour). It owns the caret from here.
      let editor =
        textBox model.FreetextContent
        |> withMode TextBoxMode.MultiLine
        |> focused

      editor.MoveToEnd()

      let updated = {
        model with
            InputMode = Insert
            InsertActivityToken = activityToken
            Lock = Some lock
            Editor = Some editor
      }

      updated, Cmd.batch [ saveLockCmd updated; scheduleAutoExit activityToken ]
  | ExitInsert ->
    // Bump the token so any in-flight debounced save is cancelled, then flush
    // the current content immediately so other users see the final edit.
    let bumped = model.FreetextSaveToken + 1

    let wasInsert =
      match model.InputMode with
      | Insert -> true
      | Normal -> false

    // Final sync from the editor (content is already synced on each edit) and
    // tear it down so Normal mode renders the wrapping paragraph instead.
    let syncedContent =
      match model.Editor with
      | Some editor -> editor.Text
      | None -> model.FreetextContent

    let updated = {
      model with
          InputMode = Normal
          FreetextContent = syncedContent
          FreetextSaveToken = bumped
          Editor = None
          Lock =
            match wasInsert with
            | true -> None
            | false -> model.Lock
    }

    let cmds =
      match wasInsert with
      | true -> [ saveFreetextCmd updated; releaseLockCmd updated ]
      | false -> []

    updated, Cmd.batch cmds
  | TypeChar c -> model |> editWith (fun editor -> editor.Insert(string c))
  | TypeBackspace -> model |> editWith (fun editor -> editor.DeleteBackward())
  | TypeDelete -> model |> editWith (fun editor -> editor.DeleteForward())
  | TypeNewLine -> model |> editWith (fun editor -> editor.InsertNewLine())
  | CaretLeft -> model |> moveCaret (fun editor -> editor.MoveLeft())
  | CaretRight -> model |> moveCaret (fun editor -> editor.MoveRight())
  | CaretUp -> model |> moveCaret (fun editor -> editor.MoveUp())
  | CaretDown -> model |> moveCaret (fun editor -> editor.MoveDown())
  | CaretHome -> model |> moveCaret (fun editor -> editor.MoveHome())
  | CaretEnd -> model |> moveCaret (fun editor -> editor.MoveEnd())
  | RemoteStateLoaded(Some state) ->
    // While the user is actively typing, ignore the freetext echo from the
    // remote — applying it would clobber characters typed since the in-flight
    // save was dispatched.
    let freetextContent =
      match model.InputMode with
      | Insert -> model.FreetextContent
      | Normal ->
        match isNull state.FreetextContent with
        | true -> ""
        | false -> state.FreetextContent

    let remoteLock =
      match isNull state.LockOwner || state.LockOwner = "" with
      | true -> None
      | false ->
        Some {
          Owner = state.LockOwner
          LockedAt = state.LockedAt
        }

    {
      model with
          FreetextContent = freetextContent
          Lock = remoteLock
    },
    []
  | RemoteStateLoaded None -> model, []
  | MaybeSaveFreetext token ->
    match token = model.FreetextSaveToken with
    | true ->
      // Refresh the lock timestamp on every save so the holder doesn't appear
      // stale to other clients while they're actively typing.
      let refreshedLock =
        match model.Lock with
        | Some l when l.Owner = model.User -> Some { l with LockedAt = Clock.nowMs () }
        | other -> other

      let updated = { model with Lock = refreshedLock }
      updated, Cmd.batch [ saveFreetextCmd updated; saveLockCmd updated ]
    | false -> model, []
  | MaybeAutoExitInsert token ->
    match model.InputMode = Insert && token = model.InsertActivityToken with
    | true -> model, Cmd.ofMsg ExitInsert
    | false -> model, []
  | StateSaved -> model, []

let subscriptions (model: Model) =
  Firebase.Notes.subscription model.Persistence.Client model.Persistence.SessionId RemoteStateLoaded

let widget (model: Model) (isFocused: bool) : IWidget =
  match model.InputMode with
  | Insert ->
    match model.Editor with
    | Some editor -> editor :> IWidget
    | None ->
      // Defensive fallback; Insert mode always carries an editor.
      textBox model.FreetextContent
      |> withMode TextBoxMode.MultiLine
      |> (focused >> withCursorAtEnd)
      :> IWidget
  | Normal ->
    ofString model.FreetextContent
    |> withOverflow Overflow.Fold
    :> IWidget
