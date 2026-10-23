module Notification

open System
open System.Runtime.InteropServices

let private windowsToastScript (title: string) (message: string) =
  let escape (value: string) =
    value.Replace("'", "''")

  sprintf
    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); $texts = $template.GetElementsByTagName('text'); $texts.Item(0).AppendChild($template.CreateTextNode('%s')) | Out-Null; $texts.Item(1).AppendChild($template.CreateTextNode('%s')) | Out-Null; $toast = [Windows.UI.Notifications.ToastNotification]::new($template); [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('tuigether').Show($toast)"
    (escape title)
    (escape message)

let private appName = "tuigether"

let send (message: string) =
  try
    let psi = Diagnostics.ProcessStartInfo()

    match RuntimeInformation.IsOSPlatform OSPlatform.OSX, RuntimeInformation.IsOSPlatform OSPlatform.Linux with
    | true, _ ->
      psi.FileName <- "osascript"
      psi.ArgumentList.Add("-e")
      psi.ArgumentList.Add(sprintf "display notification \"%s\" with title \"%s\"" message appName)
    | _, true ->
      psi.FileName <- "notify-send"
      psi.ArgumentList.Add(appName)
      psi.ArgumentList.Add(message)
    | _ ->
      psi.FileName <- "powershell"
      psi.ArgumentList.Add("-NoProfile")
      psi.ArgumentList.Add("-Command")
      psi.ArgumentList.Add(windowsToastScript appName message)

    psi.UseShellExecute <- false
    psi.CreateNoWindow <- true
    // The child shares the terminal of the full-screen app, so its warnings would draw over the view. The reads
    // drain the pipes, so a child with much output does not block.
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    let proc = Diagnostics.Process.Start psi
    proc.BeginOutputReadLine()
    proc.BeginErrorReadLine()
  with _ ->
    ()
