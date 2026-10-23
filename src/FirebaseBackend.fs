module FirebaseBackend

open Firebase.Database

let private leave
  (client: FirebaseClient)
  (sessionId: string)
  (user: string)
  (wasStarted: bool)
  (wasDriver: bool)
  : Async<unit> =
  async {
    // A Drive must not keep counting down with nobody driving.
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

let create (client: FirebaseClient) : Backend.Backend = {
  Sessions =
    { new Backend.ISessions with
        member _.Subscription = Firebase.Sessions.subscription client id

        member _.Create user title gitBranch gitRepo =
          Firebase.Sessions.create client user title gitBranch gitRepo

        member _.Delete sessionId =
          Firebase.Sessions.delete client sessionId

        member _.Start sessionId =
          Firebase.Sessions.setStatus client sessionId Session.Status.Started

        member _.DataSubscription sessionId =
          Firebase.Sessions.dataSubscription client sessionId id
    }
  Presence =
    { new Backend.IPresence with
        member _.Join sessionId user avatarName =
          Firebase.Users.join client sessionId user avatarName

        member _.Leave sessionId user wasStarted wasDriver =
          leave client sessionId user wasStarted wasDriver

        member _.Subscription sessionId subscriberTag =
          Firebase.Users.subscription client sessionId subscriberTag id
    }
}
