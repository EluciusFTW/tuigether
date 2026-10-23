module FirebaseBackend

open Firebase.Database

let private appendHistory
  (client: FirebaseClient)
  (sessionId: string)
  (eventType: Session.DriveEventType)
  (driver: string)
  (by: string)
  : Async<unit> =
  Firebase.History.append client sessionId {
    Session.DriveEvent.Type = Session.DriveEventType.toString eventType
    Session.DriveEvent.Driver = driver
    Session.DriveEvent.By = by
    Session.DriveEvent.At = Clock.nowMs ()
  }

let private appendHistoryFor
  (client: FirebaseClient)
  (sessionId: string)
  (eventType: Session.DriveEventType)
  (driver: string option)
  (by: string)
  : Async<unit> =
  match driver with
  | Some d -> appendHistory client sessionId eventType d by
  | None -> async { return () }

let private toTimerState (snapshot: Backend.TimerSnapshot) : Session.TimerState = {
  RemainingSeconds = snapshot.RemainingSeconds
  IsRunning = snapshot.IsRunning
  EndsAt = snapshot.EndsAt
}

let private toSnapshot (state: Session.TimerState) : Backend.TimerSnapshot = {
  RemainingSeconds = state.RemainingSeconds
  IsRunning = state.IsRunning
  EndsAt = state.EndsAt
  IsPaused = false
}

let private saveTimer (client: FirebaseClient) (sessionId: string) (snapshot: Backend.TimerSnapshot) =
  Firebase.Timer.save client sessionId (toTimerState snapshot)

let private driveChange
  (client: FirebaseClient)
  (sessionId: string)
  (eventType: Session.DriveEventType)
  (by: string)
  (driver: string option)
  (proposed: Backend.TimerSnapshot)
  : Async<unit> =
  [
    saveTimer client sessionId proposed
    appendHistoryFor client sessionId eventType driver by
  ]
  |> Async.Parallel
  |> Async.Ignore

let private rotateDriver
  (client: FirebaseClient)
  (sessionId: string)
  (by: string)
  (currentDriver: string option)
  (endsCurrentDrive: bool)
  (nextDriver: string option)
  (proposed: Backend.TimerSnapshot)
  : Async<unit> =
  let endCurrent =
    match endsCurrentDrive with
    | true -> appendHistoryFor client sessionId Session.DriveEventType.Switched currentDriver by
    | false -> async { return () }

  [
    Firebase.Sessions.setActiveDriver client sessionId nextDriver
    endCurrent
    appendHistoryFor client sessionId Session.DriveEventType.Started nextDriver by
    saveTimer client sessionId proposed
  ]
  |> Async.Parallel
  |> Async.Ignore

let private saveTodoItem (client: FirebaseClient) (sessionId: string) (itemId: string) (item: Session.TodoItemState) =
  Firebase.Todo.setItem client sessionId itemId item.Text item.Completed

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
      | true -> do! appendHistory client sessionId Session.DriveEventType.Paused user user
      | false -> ()
    | false -> ()

    let! result = Firebase.Users.leaveAndCheckLast client sessionId user

    match result, wasStarted with
    | Ok true, true -> do! Firebase.Sessions.setStatus client sessionId Session.Status.Finished
    | _ -> ()
  }

let create (config: Firebase.Config) : Backend.Backend =
  let client = Firebase.createClient config

  {
    Connection =
      { new Backend.IConnection with
          member _.Subscription = []
      }
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
    Driving =
      { new Backend.IDriving with
          member _.Subscription sessionId =
            Firebase.Timer.subscription client sessionId (Option.map toSnapshot)

          member _.RotateDriver sessionId by currentDriver endsCurrentDrive nextDriver proposed =
            rotateDriver client sessionId by currentDriver endsCurrentDrive nextDriver proposed

          member _.PauseDrive sessionId by driver proposed =
            driveChange client sessionId Session.DriveEventType.Paused by driver proposed

          member _.ResumeDrive sessionId by driver proposed =
            driveChange client sessionId Session.DriveEventType.Resumed by driver proposed

          member _.SkipDrive sessionId by driver proposed =
            driveChange client sessionId Session.DriveEventType.Skipped by driver proposed

          member _.EndDrive sessionId proposed =
            saveTimer client sessionId proposed

          member _.RecordFinishedDrive sessionId by driver =
            appendHistory client sessionId Session.DriveEventType.Finished driver by

          member _.EndBreak sessionId proposed =
            saveTimer client sessionId proposed

          member _.SkipBreak sessionId proposed =
            saveTimer client sessionId proposed

          member _.ClearDriver sessionId =
            Firebase.Sessions.setActiveDriver client sessionId None
      }
    DriveHistory =
      { new Backend.IDriveHistory with
          member _.Subscription sessionId =
            Firebase.History.subscription client sessionId id
      }
    Notes =
      { new Backend.INotes with
          member _.Subscription sessionId =
            Firebase.Notes.subscription client sessionId id

          member _.AcquireEditLock sessionId lock =
            Firebase.Notes.saveLock client sessionId lock.Owner lock.LockedAt

          member _.SaveDraft sessionId text lock =
            async {
              do! Firebase.Notes.saveFreetext client sessionId text

              match lock with
              | Some l -> do! Firebase.Notes.saveLock client sessionId l.Owner l.LockedAt
              | None -> ()
            }

          member _.EndEdit sessionId text =
            async {
              do! Firebase.Notes.saveFreetext client sessionId text
              do! Firebase.Notes.releaseLock client sessionId
            }
      }
    Goal =
      { new Backend.IGoal with
          member _.AcquireEditLock sessionId lock =
            Firebase.Sessions.saveGoalLock client sessionId lock.Owner lock.LockedAt

          member _.SaveDraft sessionId text lock =
            async {
              do! Firebase.Sessions.saveGoal client sessionId text

              match lock with
              | Some l -> do! Firebase.Sessions.saveGoalLock client sessionId l.Owner l.LockedAt
              | None -> ()
            }

          member _.EndEdit sessionId text =
            async {
              do! Firebase.Sessions.saveGoal client sessionId text
              do! Firebase.Sessions.releaseGoalLock client sessionId
            }
      }
    Todo =
      { new Backend.ITodo with
          member _.Subscription sessionId =
            Firebase.Todo.subscription client sessionId id

          member _.Add sessionId itemId text =
            Firebase.Todo.addItem client sessionId itemId text

          member _.ChangeText sessionId itemId item =
            saveTodoItem client sessionId itemId item

          member _.Complete sessionId itemId =
            Firebase.Todo.setCompleted client sessionId itemId true

          member _.Reopen sessionId itemId =
            Firebase.Todo.setCompleted client sessionId itemId false

          member _.Move sessionId _itemId _newPosition ((topId, top), (bottomId, bottom)) =
            [
              saveTodoItem client sessionId topId top
              saveTodoItem client sessionId bottomId bottom
            ]
            |> Async.Parallel
            |> Async.Ignore

          member _.Remove sessionId itemId =
            Firebase.Todo.deleteItem client sessionId itemId
      }
    NoteList =
      { new Backend.INoteList with
          member _.Subscription sessionId =
            Firebase.NoteList.subscription client sessionId id

          member _.Add sessionId itemId text =
            Firebase.NoteList.addItem client sessionId itemId text

          // An item is one string under its key, so a change overwrites it like an add.
          member _.Change sessionId itemId text =
            Firebase.NoteList.addItem client sessionId itemId text

          member _.Remove sessionId itemId =
            Firebase.NoteList.deleteItem client sessionId itemId
      }
    Git =
      { new Backend.IGit with
          member _.ChangeBranch sessionId branch =
            Firebase.Sessions.saveGitBranch client sessionId branch

          member _.RecordWipPush sessionId user pushedAt =
            Firebase.Sessions.saveWipPush client sessionId user pushedAt
      }
  }
