module AppView

open Spectre.Tui
open Spectre.Tui.App
open SpectreTuff.Widgets
open Application

type AppView(model: Model) =
  interface IWidget with
    member _.Render(ctx: RenderContext) =
      let panels = buildPanels model
      let slotPort = AppLayout.portFor ctx.Viewport (AppLayout.mainLayout model.LogVisible)

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
          | SessionViewPage viewModel ->
            [ SessionView.keyMap viewModel ]
            @ SessionView.helpKeyMaps viewModel
            @ [ globalKeyMap ]
          | _ -> [ globalKeyMap ]

        ctx.Render(help helpMaps |> leftAligned, slotPort AppLayout.Help)
      | KeymapModal.Modal ->
        ctx.Render(help [ keymapHintMap ] |> leftAligned, slotPort AppLayout.Help)

        match model.ShowKeymap with
        | true -> ctx.Render(KeymapModal.widget (keymapSections model))
        | false -> ()

// Spectre.Tui's AnsiTerminal keeps mutable buffer/state shared across writes
// and is not thread-safe. Subscription callbacks (Firebase observables, async
// completions) can dispatch from thread-pool threads, so view may be invoked
// concurrently. Serialize draws here.
let private renderLock = obj ()

let view (renderer: Renderer) (model: Model) _dispatch =
  lock renderLock (fun () -> renderer.Draw(fun ctx _ -> ctx.Render(AppView model)))
