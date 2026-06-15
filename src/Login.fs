module Login

open System
open Spectre.Console
open Spectre.Tui
open SpectreTuff
open SpectreTuff.Layout
open SpectreTuff.Widgets
open Firebase.Auth

// Self-contained interactive sign-in shown before the main Elmish app when no
// credentials are configured. Runs its own synchronous render/read loop against
// the shared Renderer (the app's key listener is not running yet, so there is no
// contention for Console input).

type private Field =
  | Email
  | Password

type private Status =
  | Hint
  | Info of string
  | Err of string

type private State = {
  Email: string
  Password: string
  Focus: Field
  Status: Status
}

let private innerLayout =
  layout "login-inner"
  |> splitHorizontally [|
    layout "email" |> withFixedSize (Some 1)
    layout "gap1" |> withFixedSize (Some 1)
    layout "password" |> withFixedSize (Some 1)
    layout "gap2" |> withFixedSize (Some 1)
    layout "status" |> withFixedSize (Some 1)
  |]

let private field (text: string) (placeholder: string) (isFocused: bool) : IWidget =
  let widget =
    textBox text
    |> withMode TextBoxMode.SingleLine
    |> withPlaceholder placeholder

  match isFocused with
  | true -> widget |> focused |> withCursorAtEnd :> IWidget
  | false -> widget :> IWidget

let private render (renderer: Renderer) (state: State) =
  let emailWidget = field state.Email "Email" (state.Focus = Email)
  // Mask the password: keep the real value in state, display only asterisks.
  let passwordWidget = field (String('*', state.Password.Length)) "Password" (state.Focus = Password)

  let statusWidget =
    match state.Status with
    | Hint -> ofString "Tab to switch · Enter to sign in · Esc to quit" :> IWidget
    | Info msg -> paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Grey)) msg ] ] :> IWidget
    | Err msg -> paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Red)) msg ] ] :> IWidget

  let content =
    { new IWidget with
        member _.Render(ctx) =
          let port = getPort ctx.Viewport innerLayout
          ctx.Render(emailWidget, port "email")
          ctx.Render(passwordWidget, port "password")
          ctx.Render(statusWidget, port "status")
    }

  let boxed =
    box (Look.fromColor Color.Green)
    |> withTitle "tuigether — sign in"
    |> withInnerWidget content
    :> IWidget

  renderer.Draw(fun ctx _ -> ctx.Render(popup 60 9 |> withPopupContent boxed :> IWidget))

// Drives the login loop until the user authenticates (Some user) or quits with
// Esc (None).
let run (renderer: Renderer) (authClient: FirebaseAuthClient) : User option =
  let trimEnd (s: string) =
    match s.Length with
    | 0 -> s
    | n -> s.Substring(0, n - 1)

  let rec loop (state: State) : User option =
    render renderer state
    let key = Console.ReadKey true

    match key.Key with
    | ConsoleKey.Escape -> None
    | ConsoleKey.Tab
    | ConsoleKey.UpArrow
    | ConsoleKey.DownArrow ->
      let next =
        match state.Focus with
        | Email -> Password
        | Password -> Email

      loop { state with Focus = next }
    | ConsoleKey.Enter ->
      match state.Focus with
      | Email when state.Password = "" -> loop { state with Focus = Password }
      | _ ->
        let email = state.Email.Trim()

        match email = "" || state.Password = "" with
        | true -> loop { state with Status = Err "Enter both email and password." }
        | false ->
          render renderer { state with Status = Info "Signing in…" }

          match Auth.signIn authClient email state.Password |> Async.RunSynchronously with
          | Ok user -> Some user
          | Error msg -> loop { state with Status = Err msg }
    | ConsoleKey.Backspace ->
      let next =
        match state.Focus with
        | Email -> { state with Email = trimEnd state.Email; Status = Hint }
        | Password -> { state with Password = trimEnd state.Password; Status = Hint }

      loop next
    | _ when key.KeyChar <> '\000' && not (Char.IsControl key.KeyChar) ->
      let next =
        match state.Focus with
        | Email -> { state with Email = state.Email + string key.KeyChar; Status = Hint }
        | Password -> { state with Password = state.Password + string key.KeyChar; Status = Hint }

      loop next
    | _ -> loop state

  loop {
    Email = ""
    Password = ""
    Focus = Email
    Status = Hint
  }
