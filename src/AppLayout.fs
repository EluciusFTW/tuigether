module AppLayout

open SpectreTuff.Layout

/// Named regions of the application's layout trees. Using a DU instead of raw
/// strings means a typo can't silently produce an empty port, and the set of
/// slots is discoverable.
type Slot =
  | Main
  | Top
  | Content
  | Keys
  | Log
  | Help
  | Footer
  | Connection
  | PanelInner

module Slot =
  let name =
    function
    | Main -> "main"
    | Top -> "top"
    | Content -> "content"
    | Keys -> "keys"
    | Log -> "log"
    | Help -> "help"
    | Footer -> "footer"
    | Connection -> "connection"
    | PanelInner -> "panel-inner"

let private named slot =
  layout (Slot.name slot)

let panelInnerLayout =
  named PanelInner
  |> splitHorizontally [| named Content; named Keys |> withFixedSize (Some 1) |]

// `connectionWidth` is 0 when the footer shows no connection state.
let mainLayout (logVisible: bool) (connectionWidth: int) =
  named Main
  |> splitHorizontally [|
    named Top
    |> splitVertically [|
      named Content |> withRatio 3
      named Log
      |> withRatio 1
      |> (match logVisible with
          | true -> show
          | false -> hide)
    |]
    named Footer
    |> withFixedSize (Some 1)
    |> splitVertically [|
      named Help
      // Spectre.Tui rejects a fixed size below 1.
      match connectionWidth with
      | width when width < 1 -> named Connection |> hide
      | width -> named Connection |> withFixedSize (Some width)
    |]
  |]

/// Typed slot lookup over a layout tree: resolves a Slot to its rendering
/// rectangle, so callers never pass raw slot strings.
let portFor viewport layoutTree =
  let port = getPort viewport layoutTree
  fun slot -> port (Slot.name slot)
