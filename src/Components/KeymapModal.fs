module KeymapModal

open System
open Spectre.Console
open Spectre.Tui
open Spectre.Tui.App
open SpectreTuff
open SpectreTuff.Widgets

type Mode =
  | Inline
  | Modal

let mode = Modal

let private keyName (press: KeyPress) =
  match press.Key with
  | Key.Up -> "↑"
  | Key.Down -> "↓"
  | Key.Left -> "←"
  | Key.Right -> "→"
  | Key.Enter -> "Enter"
  | Key.Escape -> "Esc"
  | Key.Tab -> "Tab"
  | Key.Space -> "Space"
  | Key.Backspace -> "Backspace"
  | Key.Delete -> "Del"
  | Key.Insert -> "Ins"
  | Key.Home -> "Home"
  | Key.End -> "End"
  | Key.PageUp -> "PgUp"
  | Key.PageDown -> "PgDn"
  | Key.None ->
    match press.Character.HasValue with
    | true -> string press.Character.Value
    | false -> ""
  | other -> string other

let private withModifiers (press: KeyPress) (name: string) =
  [
    KeyModifier.Ctrl, "Ctrl+"
    KeyModifier.Alt, "Alt+"
    KeyModifier.Shift, "Shift+"
  ]
  |> List.filter (fun (modifier, _) -> press.Modifiers.HasFlag modifier)
  |> List.map snd
  |> String.concat ""
  |> fun prefix -> prefix + name

let private formatKeys (binding: KeyBinding) =
  binding.Keys
  |> Seq.map (fun press -> withModifiers press (keyName press))
  |> Seq.filter (fun name -> name <> "")
  |> String.concat "/"

type private Row =
  | Header of title: string
  | Entry of keys: string * help: string
  | Blank

let private rowsFor (sections: (string * IKeyMap) list) =
  sections
  |> List.map (fun (title, keyMap) ->
    let entries =
      keyMap.Help()
      |> Seq.filter (_.Enabled)
      |> Seq.map (fun binding -> Entry(formatKeys binding, binding.Help))
      |> Seq.toList

    match entries with
    | [] -> []
    | _ -> Header title :: entries)
  |> List.filter (List.isEmpty >> not)
  |> function
    | [] -> []
    | sections -> sections |> List.reduce (fun acc section -> acc @ [ Blank ] @ section)

let private modalWidget (rows: Row list) : IWidget =
  { new IWidget with
      member _.Render(ctx: RenderContext) =
        let keyWidth =
          rows
          |> List.map (function
            | Entry(keys, _) -> keys.Length
            | _ -> 0)
          |> List.max

        let lines =
          rows
          |> List.map (function
            | Header title -> Text.line [ Text.styledSpan (Nullable(Style Color.Aqua)) title ]
            | Entry(keys, helpText) ->
              Text.line [
                Text.styledSpan (Nullable(Style Color.White)) (sprintf "  %s   " (keys.PadRight keyWidth))
                Text.styledSpan (Nullable(Style Color.Grey)) helpText
              ]
            | Blank -> Text.line [ Text.span "" ])

        let contentWidth =
          rows
          |> List.map (function
            | Header title -> title.Length
            | Entry(_, helpText) -> 2 + keyWidth + 3 + helpText.Length
            | Blank -> 0)
          |> List.max

        let w = min (ctx.Viewport.Width - 4) (max 36 (contentWidth + 4))
        let h = min (ctx.Viewport.Height - 4) (rows.Length + 2)

        let boxed =
          box (Look.fromColor Color.Aqua)
          |> withTitle "Keymaps"
          |> withInnerWidget (paragraph lines :> IWidget)
          :> IWidget

        ctx.Render(popup w h |> withPopupContent boxed :> IWidget)
  }

let private emptyWidget: IWidget =
  { new IWidget with
      member _.Render _ =
        ()
  }

let widget (sections: (string * IKeyMap) list) : IWidget =
  match rowsFor sections with
  | [] -> emptyWidget
  | rows -> modalWidget rows
