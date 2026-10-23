open System
open Dependencies
open SpectreTuff.Elmish

match Config.load () with
| Error message ->
  eprintfn "%s" message
  exit 1
| Ok settings ->
  let notify =
    match settings.NotificationsEnabled with
    | true -> Notification.send
    | false -> fun _ -> ()

  let deps: Dependencies = {
    Backend =
      FirebaseBackend.create {
        Url = settings.FirebaseUrl
        Secret = settings.FirebaseSecret
      }
    Notify = notify
  }

  Elmish.Program.mkProgram
    (Application.init deps settings.TuigetherUser)
    (Application.update deps settings.TuigetherUser)
    AppView.view
  |> Elmish.Program.withSubscription (fun model ->
    Subscriptions.keys Application.InputMsg
    @ Subscriptions.tick (TimeSpan.FromMilliseconds 200.0) Application.Tick
    @ Application.subscriptions model)
  |> Elmish.Program.withTrace Application.traceToLog
  |> Tui.run
