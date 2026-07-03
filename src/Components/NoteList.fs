module NoteList

open System
open System.Runtime.InteropServices
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
  | Edit of TextEditing.EditAction
  | ConfirmAdd
  | CancelAdd
  | Delete
  | Copy
  | RemoteStateLoaded of Session.ListState option
  | StateSaved

let private normalBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.UpArrow "up" Up
  KeyBinding.createSpecial ConsoleKey.DownArrow "down" Down
  KeyBinding.create 'a' "add" StartAdd
  KeyBinding.create 'x' "delete" Delete
  KeyBinding.create 'c' "copy" Copy
]

let private addingItemBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Enter "confirm" ConfirmAdd
  KeyBinding.createSpecial ConsoleKey.Escape "cancel" CancelAdd
]

let handleKey (key: ConsoleKeyInfo) (model: Model) : Msg option =
  match model.InputMode with
  | AddingItem _ ->
    match key.Key with
    | ConsoleKey.Escape -> Some CancelAdd
    | ConsoleKey.Enter -> Some ConfirmAdd
    | _ -> TextEditing.keyToAction false key |> Option.map Edit
  | Normal ->
    match key.Key with
    | ConsoleKey.UpArrow -> Some Up
    | ConsoleKey.DownArrow -> Some Down
    | _ ->
      match key.KeyChar with
      | 'a' -> Some StartAdd
      | 'x' -> Some Delete
      | 'c' -> Some Copy
      | 'j' -> Some Down
      | 'k' -> Some Up
      | _ -> None

let capturesInput (model: Model) =
  match model.InputMode with
  | AddingItem _ -> true
  | Normal -> false

let keyMap (model: Model) =
  let bindings =
    match model.InputMode with
    | AddingItem _ -> addingItemBindings
    | Normal -> normalBindings

  KeyBinding.toKeyMap bindings model

// Linux: order by display server. xclip exits non-zero under Wayland (no
// throw), so wrong-server tool is last resort.
let private clipboardCandidates () : (string * string) list =
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

let private copyToClipboard (text: string) : Result<string, string> =
  let rec attempt remaining =
    match remaining with
    | [] -> Error "no working clipboard tool found (install xclip, xsel, or wl-copy)"
    | (fileName, arguments) :: rest ->
      match tryCopyWith fileName arguments text with
      | true -> Ok fileName
      | false -> attempt rest

  attempt (clipboardCandidates ())

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

    { model with InputMode = AddingItem editor }, []
  | Edit action ->
    match model.InputMode with
    | AddingItem editor ->
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
    | Normal -> model, []
  | CancelAdd -> { model with InputMode = Normal }, []
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
      match copyToClipboard model.Items.[model.SelectedIndex].Text with
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
// to black-on-green instead of the default list item's yellow-on-blue.
type private NoteListItem(text: string) =
  interface IListWidgetItem with
    member _.CreateText(isSelected) =
      let style =
        match isSelected with
        | true -> Style(Color.Black, Color.Green)
        | false -> Style(Color.Green)

      Text(LineExtensions.FromString(" • " + text, style))

let widget (model: Model) (isFocused: bool) : IWidget =
  let items = model.Items |> List.map (fun item -> NoteListItem item.Text)

  let listWidget =
    list items
    |> withSelectedIndex (
      match isFocused, items with
      | false, _
      | _, [] -> None
      | _ -> Some model.SelectedIndex
    )
    |> wrapAround
    :> IWidget

  match model.InputMode with
  | AddingItem editor ->
    { new IWidget with
        member _.Render(ctx) =
          ctx.Render(listWidget)

          let boxedInput =
            box (Look.fromColor Color.Green)
            |> withTitle "New item"
            |> withInnerWidget (editor :> IWidget)
            :> IWidget

          ctx.Render(popup 44 3 |> withPopupContent boxedInput :> IWidget)
    }
  | Normal -> listWidget
