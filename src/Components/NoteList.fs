module NoteList

open System
open Elmish
open Firebase.Database
open Spectre.Console
open Spectre.Tui
open Keymap
open SpectreTuff
open SpectreTuff.Widgets

type InputMode =
  | Normal
  // Carries the live editor while adding, so it owns the caret and does all
  // at-cursor editing; the item text is read back from it on confirm.
  | AddingItem of TextBoxWidget
  // Carries the edited item's Id plus a live editor seeded with its current
  // text; on confirm the text is read back and written to that item.
  | EditingItem of id: string * editor: TextBoxWidget

// Items carry their Firebase push-ID so deletes target a stable key and
// concurrent multi-user edits do not collide.
type Item = { Id: string; Text: string }

type Model = {
  InputMode: InputMode
  Items: Item list
  SelectedIndex: int
  Persistence: Firebase.Persistence
}

type Msg =
  | Up
  | Down
  | StartAdd
  | StartEdit
  | Edit of TextEditing.EditAction
  | ConfirmAdd
  | CancelAdd
  | ConfirmEdit
  | Delete
  | Copy
  | RemoteStateLoaded of Session.ListState option
  | StateSaved

let private normalBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.UpArrow "up" Up
  KeyBinding.createSpecial ConsoleKey.DownArrow "down" Down
  KeyBinding.create 'a' "add" StartAdd
  KeyBinding.create 'e' "edit" StartEdit
  KeyBinding.create 'x' "delete" Delete
  KeyBinding.create 'c' "copy" Copy
]

let private addingItemBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Enter "confirm" ConfirmAdd
  KeyBinding.createSpecial ConsoleKey.Escape "cancel" CancelAdd
]

let private editingItemBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Enter "save" ConfirmEdit
  KeyBinding.createSpecial ConsoleKey.Escape "save & close" ConfirmEdit
]

let handleKey (key: ConsoleKeyInfo) (model: Model) : Msg option =
  match model.InputMode with
  | AddingItem _ ->
    match key.Key with
    | ConsoleKey.Escape -> Some CancelAdd
    | ConsoleKey.Enter -> Some ConfirmAdd
    | _ -> TextEditing.keyToAction false key |> Option.map Edit
  | EditingItem _ ->
    match key.Key with
    | ConsoleKey.Escape -> Some ConfirmEdit
    | ConsoleKey.Enter -> Some ConfirmEdit
    | _ -> TextEditing.keyToAction false key |> Option.map Edit
  | Normal ->
    match key.Key with
    | ConsoleKey.UpArrow -> Some Up
    | ConsoleKey.DownArrow -> Some Down
    | _ ->
      match key.KeyChar with
      | 'a' -> Some StartAdd
      | 'e' -> Some StartEdit
      | 'x' -> Some Delete
      | 'c' -> Some Copy
      | 'j' -> Some Down
      | 'k' -> Some Up
      | _ -> None

let handlePaste (text: string) (model: Model) : Msg option =
  match model.InputMode with
  | AddingItem _
  | EditingItem _ -> Some(Edit(TextEditing.pasteAction false text))
  | Normal -> None

let capturesInput (model: Model) =
  match model.InputMode with
  | AddingItem _
  | EditingItem _ -> true
  | Normal -> false

let keyMap (model: Model) =
  let bindings =
    match model.InputMode with
    | AddingItem _ -> addingItemBindings
    | EditingItem _ -> editingItemBindings
    | Normal -> normalBindings

  KeyBinding.toKeyMap bindings model

let init (client: FirebaseClient) (sessionId: string) = {
  InputMode = Normal
  Items = []
  SelectedIndex = 0
  Persistence = {
    Client = client
    SessionId = sessionId
  }
}

let private addItemCmd (model: Model) (item: Item) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> Firebase.NoteList.addItem model.Persistence.Client model.Persistence.SessionId item.Id item.Text)
    ()
    (fun () -> StateSaved)

let private deleteItemCmd (model: Model) (itemId: string) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> Firebase.NoteList.deleteItem model.Persistence.Client model.Persistence.SessionId itemId)
    ()
    (fun () -> StateSaved)

let update msg model =
  match msg with
  | Up ->
    let count = model.Items.Length

    match count with
    | 0 -> model, []
    | _ ->
      {
        model with
            SelectedIndex = (model.SelectedIndex - 1 + count) % count
      },
      []
  | Down ->
    let count = model.Items.Length

    match count with
    | 0 -> model, []
    | _ ->
      {
        model with
            SelectedIndex = (model.SelectedIndex + 1) % count
      },
      []
  | StartAdd ->
    let editor =
      textBox ""
      |> withMode TextBoxMode.SingleLine
      |> withPlaceholder "Enter item text…"
      |> focused

    {
      model with
          InputMode = AddingItem editor
    },
    []
  | StartEdit ->
    match model.Items with
    | [] -> model, []
    | _ ->
      let item = model.Items.[model.SelectedIndex]

      let editor = textBox item.Text |> withMode TextBoxMode.SingleLine |> focused

      editor.MoveToEnd()

      {
        model with
            InputMode = EditingItem(item.Id, editor)
      },
      []
  | Edit action ->
    match model.InputMode with
    | AddingItem editor
    | EditingItem(_, editor) ->
      TextEditing.apply action editor
      model, []
    | Normal -> model, []
  | ConfirmAdd ->
    match model.InputMode with
    | AddingItem editor ->
      let newText =
        match editor.Text.Trim() with
        | "" -> "New note"
        | s -> s

      // Push IDs are chronologically sortable, so new items always append.
      let newItem = {
        Id = Firebase.PushId.generate ()
        Text = newText
      }

      let newItems = model.Items @ [ newItem ]

      let updated = {
        model with
            Items = newItems
            SelectedIndex = newItems.Length - 1
            InputMode = Normal
      }

      updated, addItemCmd updated newItem
    | _ -> model, []
  | CancelAdd -> { model with InputMode = Normal }, []
  | ConfirmEdit ->
    match model.InputMode with
    | EditingItem(id, editor) ->
      // Blank edits are ignored so an accidental clear can't wipe an item; the
      // original text stays put.
      match editor.Text.Trim() with
      | "" -> { model with InputMode = Normal }, []
      | newText ->
        let newItems =
          model.Items
          |> List.map (fun it ->
            match it.Id = id with
            | true -> { it with Text = newText }
            | false -> it)

        let updated = {
          model with
              Items = newItems
              InputMode = Normal
        }

        // The item can vanish mid-edit if another user deletes it; skip the write.
        match newItems |> List.tryFind (fun it -> it.Id = id) with
        | Some edited -> updated, addItemCmd updated edited
        | None -> updated, []
    | _ -> model, []
  | Delete ->
    match model.Items with
    | [] -> model, []
    | _ ->
      let removed = model.Items.[model.SelectedIndex]
      let newItems = model.Items |> List.removeAt model.SelectedIndex

      let newIndex =
        match newItems with
        | [] -> 0
        | _ -> min model.SelectedIndex (newItems.Length - 1)

      let updated = {
        model with
            Items = newItems
            SelectedIndex = newIndex
      }

      updated, deleteItemCmd updated removed.Id
  | Copy ->
    match model.Items with
    | [] -> ()
    | _ ->
      match Clipboard.copy model.Items.[model.SelectedIndex].Text with
      | Ok tool -> Log.line (sprintf "copied list item to clipboard via %s" tool)
      | Error message -> Log.line (sprintf "clipboard copy failed: %s" message)

    model, []
  | RemoteStateLoaded(Some state) ->
    let items =
      match isNull state.Items with
      | true -> []
      | false ->
        state.Items
        |> Seq.sortBy (fun kvp -> kvp.Key)
        |> Seq.map (fun kvp -> { Id = kvp.Key; Text = kvp.Value })
        |> Seq.toList

    let selectedIndex =
      match items with
      | [] -> 0
      | _ -> model.SelectedIndex |> max 0 |> min (items.Length - 1)

    {
      model with
          Items = items
          SelectedIndex = selectedIndex
    },
    []
  | RemoteStateLoaded None -> model, []
  | StateSaved -> model, []

let subscriptions (model: Model) =
  Firebase.NoteList.subscription model.Persistence.Client model.Persistence.SessionId RemoteStateLoaded

// List items render green, matching the Todo widget. The selected row inverts
// to black-on-green instead of the default list item's yellow-on-blue. Items
// carry pre-wrapped lines so long text folds into a hanging-indented block
// instead of being truncated.
type private NoteListItem(lines: string list) =
  interface IListWidgetItem with
    member _.CreateText(isSelected) =
      let style =
        match isSelected with
        | true -> Style(Color.Black, Color.Green)
        | false -> Style(Color.Green)

      TextExtensions.FromString(String.Join("\n", lines), style)

let private bullet = " • "

let private items (model: Model) (width: int) (height: int) =
  model.Items
  |> List.map (fun item -> NoteListItem(Str.wrapHanging bullet width item.Text |> Str.capLines height width))

let widget (model: Model) (isFocused: bool) : IWidget =
  // Wrapping needs the width the panel actually hands us, which is only known
  // once we are rendering — so the list is built inside Render.
  let renderList (ctx: RenderContext) =
    let items = items model ctx.Viewport.Width ctx.Viewport.Height

    let listWidget =
      list items
      |> withSelectedIndex (
        match isFocused, items with
        | false, _
        | _, [] -> None
        | _ -> Some model.SelectedIndex
      )
      |> wrapAround

    ctx.Render(listWidget)

  let renderEditorPopup (ctx: RenderContext) (title: string) (editor: TextBoxWidget) =
    let boxedInput =
      box (Look.fromColor Color.Green)
      |> withTitle title
      |> withInnerWidget (editor :> IWidget)
      :> IWidget

    ctx.Render(popup 44 3 |> withPopupContent boxedInput :> IWidget)

  { new IWidget with
      member _.Render(ctx) =
        renderList ctx

        match model.InputMode with
        | AddingItem editor -> renderEditorPopup ctx "New item" editor
        | EditingItem(_, editor) -> renderEditorPopup ctx "Edit item" editor
        | Normal -> ()
  }
