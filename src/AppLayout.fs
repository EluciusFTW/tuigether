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
    | PanelInner -> "panel-inner"

let private named slot =
  layout (Slot.name slot)

let panelInnerLayout =
  named PanelInner
  |> splitHorizontally [| named Content; named Keys |> withFixedSize (Some 1) |]

let mainLayout (logVisible: bool) =
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
    named Help |> withFixedSize (Some 1)
  |]

/// Typed slot lookup over a layout tree: resolves a Slot to its rendering
/// rectangle, so callers never pass raw slot strings.
let portFor viewport layoutTree =
  let port = getPort viewport layoutTree
  fun slot -> port (Slot.name slot)
