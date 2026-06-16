module SessionOrchestration

open Firebase.Database

/// Side effects that must complete when a user leaves (or quits) a session.
/// Awaited by the caller (not dispatched as a message) so they finish even on
/// quit, before the app exits.
let leaveAndFinalize
  (client: FirebaseClient)
  (sessionId: string)
  (user: string)
  (wasStarted: bool)
  (wasDriver: bool)
  : Async<unit> =
  async {
    // If the active driver is leaving, pause the running drive first so it stops
    // counting down with nobody driving.
    match wasDriver with
    | true ->
      let! paused = Firebase.Timer.pauseIfRunning client sessionId

      match paused with
      | true ->
        do!
          Firebase.History.append client sessionId {
            Session.DriveEvent.Type = Session.DriveEventType.toString Session.DriveEventType.Paused
            Session.DriveEvent.Driver = user
            Session.DriveEvent.By = user
            Session.DriveEvent.At = Clock.nowMs ()
          }
      | false -> ()
    | false -> ()

    let! result = Firebase.Users.leaveAndCheckLast client sessionId user

    match result, wasStarted with
    | Ok true, true -> do! Firebase.Sessions.setStatus client sessionId Session.Status.Finished
    | _ -> ()
  }
