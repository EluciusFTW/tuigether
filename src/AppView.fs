module AppView

open System
open Spectre.Console
open Spectre.Tui
open Spectre.Tui.App
open SpectreTuff
open SpectreTuff.Widgets
open Tuisweeper
open Tui48
open Application

let private connectionText (state: Backend.ConnectionState) =
  match state with
  | Backend.Connecting -> "server: connecting…", Color.Yellow
  | Backend.Connected -> "server: connected", Color.Green
  | Backend.Disconnected -> "server: connection lost, retrying…", Color.Red
  | Backend.Refused reason -> sprintf "server: %s" reason, Color.Red

type AppView(model: Model) =
  interface IWidget with
    member _.Render(ctx: RenderContext) =
      let panels = buildPanels model
      let connection = model.Connection |> Option.map connectionText

      let connectionWidth =
        connection
        // A long refusal reason must leave room for the key help.
        |> Option.map (fun (text, _) -> min (text.Length + 1) (ctx.Viewport.Width / 2))
        |> Option.defaultValue 0

      let slotPort = AppLayout.portFor ctx.Viewport (AppLayout.mainLayout model.LogVisible connectionWidth)

      for panel in panels do
        let composedWidget =
          { new IWidget with
              member _.Render(ctx) =
                match panel.Boxed, KeymapModal.mode with
                | true, KeymapModal.Inline ->
                  let port = AppLayout.portFor ctx.Viewport AppLayout.panelInnerLayout
                  ctx.Render(panel.Widget, port AppLayout.Content)
                  ctx.Render(help [ panel.KeyMap ] |> leftAligned, port AppLayout.Keys)
                | _ -> ctx.Render(panel.Widget, ctx.Viewport)
          }

        let renderedPanel: IWidget =
          match panel.Boxed with
          | true ->
            let focusState =
              match panel.CapturesInput, panel.Focused with
              | true, _ -> Capturing
              | _, true -> Focused
              | _ -> Unfocused

            focusableBox panel.Title panel.Number focusState composedWidget :> IWidget
          | false -> composedWidget

        ctx.Render(renderedPanel, slotPort panel.LayoutSlot)

      match model.LogVisible with
      | true -> Log.view model.LogModel ctx (slotPort AppLayout.Log)
      | false -> ()

      match KeymapModal.mode with
      | KeymapModal.Inline ->
        let helpMaps =
          match model.Page with
          | SessionViewPage viewModel -> [ SessionView.keyMap viewModel; globalKeyMap ]
          | _ -> [ globalKeyMap ]

        ctx.Render(help helpMaps |> leftAligned, slotPort AppLayout.Help)
      | KeymapModal.Modal ->
        ctx.Render(help [ keymapHintMap ] |> leftAligned, slotPort AppLayout.Help)

        match model.ShowKeymap with
        | true -> ctx.Render(KeymapModal.widget (keymapSections model))
        | false -> ()

      match connection with
      | Some(text, color) ->
        ctx.Render(
          paragraph [ Text.line [ Text.styledSpan (Nullable(Style color)) text ] ],
          slotPort AppLayout.Connection
        )
      | None -> ()

      match model.OpenGame with
      | Some SweeperGame -> ctx.Render(Tuisweeper.view model.Sweeper ignore)
      | Some TileGame -> ctx.Render(Tui48.view model.Tile ignore)
      | None -> ()

let view (model: Model) _dispatch =
  AppView model
