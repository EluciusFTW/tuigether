module Notes

open System
open Elmish
open Spectre.Tui
open SpectreTuff.Keymap
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
  // An acquire of the Edit lock is on its way. Insert mode starts when the backend accepts it.
  AwaitingEditLock: bool
  User: string
  Backend: Backend.Backend
  SessionId: string
  // Live editor, present only while in Insert mode. It owns the caret and does
  // all at-cursor editing; FreetextContent is kept in sync from it on each edit
  // and remains the source of truth for persistence and Normal-mode rendering.
  Editor: TextBoxWidget option
}

type Msg =
  | EnterInsert
  | EditLockAnswered of proposed: Locking.Lock * Result<unit, string>
  | ExitInsert
  | Edit of TextEditing.EditAction
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
    // keyToAction drops control keys such as Tab (whose literal insertion garbled
    // the editor); Escape stays here so it can exit insert mode.
    match key.Key with
    | ConsoleKey.Escape -> Some ExitInsert
    | _ -> TextEditing.keyToAction true key |> Option.map Edit
  | Normal when model.AwaitingEditLock -> None
  | Normal ->
    match key.KeyChar with
    | 'i' -> Some EnterInsert
    | _ -> None

let handlePaste (text: string) (model: Model) : Msg option =
  match model.InputMode with
  | Insert -> Some(Edit(TextEditing.pasteAction true text))
  | Normal -> None

let capturesInput (model: Model) =
  match model.InputMode with
  | Insert -> true
  | Normal -> model.AwaitingEditLock

let keyMap (model: Model) =
  let bindings =
    match model.InputMode with
    | Insert -> insertModeBindings
    | Normal -> normalBindings

  KeyBinding.toKeyMap bindings model

let init (backend: Backend.Backend) (sessionId: string) (user: string) = {
  InputMode = Normal
  FreetextContent = ""
  FreetextSaveToken = 0
  InsertActivityToken = 0
  Lock = None
  AwaitingEditLock = false
  User = user
  Backend = backend
  SessionId = sessionId
  Editor = None
}

let private saveDraftCmd (model: Model) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> model.Backend.Notes.SaveDraft model.SessionId model.FreetextContent model.Lock)
    ()
    (fun () -> StateSaved)

// Debounce freetext writes: each typed character bumps a token and schedules a
// MaybeSaveFreetext for that token after a short idle delay. The actual save
// only fires if the token still matches the latest one — so a burst of fast
// keystrokes collapses into a single write at the end.
let private scheduleFreetextSave (token: int) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep freetextDebounceMs }) () (fun () -> MaybeSaveFreetext token)

// Auto-exit Insert mode after autoExitInsertMs of no typing activity. Every
// keystroke bumps the activity token and schedules a fresh check; only the
// scheduled check whose token still matches actually exits.
let private scheduleAutoExit (token: int) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep autoExitInsertMs }) () (fun () -> MaybeAutoExitInsert token)

// The server backend shows its rejection. A Firebase rejection stays silent: the key hint shows the holder.
let private acquireLockCmd (model: Model) (lock: Locking.Lock) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> model.Backend.Notes.AcquireEditLock model.SessionId lock model.Lock) () (fun answer ->
    EditLockAnswered(lock, answer))

let private endEditCmd (model: Model) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> model.Backend.Notes.EndEdit model.SessionId model.FreetextContent) () (fun () ->
    StateSaved)

let update msg model =
  match msg with
  | EnterInsert ->
    match model.AwaitingEditLock, model.InputMode with
    | true, _
    | _, Insert -> model, []
    | false, Normal ->
      let lock = {
        Owner = model.User
        LockedAt = Clock.nowMs ()
      }

      { model with AwaitingEditLock = true }, acquireLockCmd model lock
  | EditLockAnswered(_, Error _) -> { model with AwaitingEditLock = false }, []
  | EditLockAnswered(lock, Ok()) ->
    match model.InputMode with
    | Insert -> { model with AwaitingEditLock = false }, []
    | Normal ->
      let activityToken = model.InsertActivityToken + 1

      // Build the live editor from the current content and place the caret at
      // the end (matching the previous behaviour). It owns the caret from here.
      let editor = textBox model.FreetextContent |> withMode TextBoxMode.MultiLine |> focused

      editor.MoveToEnd()

      {
        model with
            InputMode = Insert
            AwaitingEditLock = false
            InsertActivityToken = activityToken
            Lock = Some lock
            Editor = Some editor
      },
      scheduleAutoExit activityToken
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
      | true -> [ endEditCmd updated ]
      | false -> []

    updated, Cmd.batch cmds
  | Edit action ->
    match model.Editor with
    | None -> model, []
    | Some editor ->
      TextEditing.apply action editor
      let activityToken = model.InsertActivityToken + 1

      // Text edits sync back for persistence and schedule a debounced save; pure
      // caret moves only refresh the idle auto-exit timer.
      match TextEditing.isMutation action with
      | true ->
        let bumped = model.FreetextSaveToken + 1

        let updated = {
          model with
              FreetextContent = editor.Text
              FreetextSaveToken = bumped
              InsertActivityToken = activityToken
        }

        updated, Cmd.batch [ scheduleFreetextSave bumped; scheduleAutoExit activityToken ]
      | false ->
        {
          model with
              InsertActivityToken = activityToken
        },
        scheduleAutoExit activityToken
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
      updated, saveDraftCmd updated
    | false -> model, []
  | MaybeAutoExitInsert token ->
    match model.InputMode = Insert && token = model.InsertActivityToken with
    | true -> model, Cmd.ofMsg ExitInsert
    | false -> model, []
  | StateSaved -> model, []

let subscriptions (model: Model) =
  model.Backend.Notes.Subscription model.SessionId |> Subs.map RemoteStateLoaded

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
  | Normal -> ofString model.FreetextContent |> withOverflow Overflow.Fold :> IWidget
