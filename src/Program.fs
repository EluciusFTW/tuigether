open System
open Dependencies

match Config.load () with
| Error message ->
  eprintfn "%s" message
  exit 1
| Ok settings ->
  let authClient = Auth.createClient settings

  // Silent path: when credentials are configured, sign in before touching the
  // terminal so any failure surfaces on stderr rather than inside the TUI.
  let preAuthedUser =
    match settings.Credentials with
    | Some(email, password) ->
      match Auth.signIn authClient email password |> Async.RunSynchronously with
      | Ok user -> Some user
      | Error message ->
        eprintfn "Login failed: %s" message
        exit 1
    | None -> None

  let terminal = Spectre.Tui.Terminal.Create()
  // Work around ConPTY alt-screen sizing bug: exit and re-enter alt-screen
  // so ConPTY allocates the buffer with the real window dimensions.
  Console.Write "\x1b[?1049l"
  Console.Out.Flush()
  System.Threading.Thread.Sleep 30
  Console.Write "\x1b[?1049h"
  Console.Out.Flush()

  let renderer = Spectre.Tui.Renderer terminal
  renderer.NoTargetFps()

  // Interactive path: prompt for credentials when none were configured. Quitting
  // the login screen leaves the alt-screen and exits cleanly.
  let user =
    match preAuthedUser with
    | Some u -> u
    | None ->
      match Login.run renderer authClient with
      | Some u -> u
      | None ->
        Console.Write "\x1b[?1049l"
        Console.Out.Flush()
        exit 0

  let client = Firebase.createClient settings.FirebaseUrl user
  let displayName = Auth.identity user

  let notify =
    match settings.NotificationsEnabled with
    | true -> Notification.send
    | false -> fun _ -> ()

  let deps: Dependencies = { Client = client; Notify = notify }

  Elmish.Program.mkProgram
    (Application.init client displayName)
    (Application.update deps displayName)
    (Application.view renderer)
  |> Elmish.Program.withSubscription (fun model ->
    Input.subscription Application.InputMsg model
    @ Tick.subscription (TimeSpan.FromMilliseconds 200.0) Application.Tick model
    @ Application.subscriptions model)
  |> Elmish.Program.withTrace Application.traceToLog
  |> Elmish.Program.run

  Application.exitEvent.Wait()
  Console.Clear()
