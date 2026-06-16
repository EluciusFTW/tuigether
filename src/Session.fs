module Session

open System.Collections.Generic

[<CLIMutable>]
type UserPresence = { Avatar: string; Mood: string }

[<RequireQualifiedAccess>]
type Status =
  | Created
  | Started
  | Finished

module Status =
  let toString =
    function
    | Status.Created -> "Created"
    | Status.Started -> "Started"
    | Status.Finished -> "Finished"

  let fromString (s: string) =
    match s with
    | "Started" -> Status.Started
    | "Finished" -> Status.Finished
    | _ -> Status.Created

[<CLIMutable>]
type Data = {
  Title: string
  Goal: string
  StartedAt: int64
  WorkStartedAt: int64
  Creator: string
  ActiveDriver: string
  Status: string
  GoalLockOwner: string
  GoalLockedAt: int64
  GitBranch: string
  GitRepo: string
  LastWipPushAt: int64
  LastWipPushBy: string
}

[<CLIMutable>]
type NotesState = {
  FreetextContent: string
  LockOwner: string
  LockedAt: int64
}

[<CLIMutable>]
type ListState = { Items: Dictionary<string, string> }

[<CLIMutable>]
type TimerState = {
  RemainingSeconds: int
  IsRunning: bool
  // Absolute unix-ms instant the running countdown reaches zero. Authoritative
  // source for the remaining time so every client derives the same value from
  // the wall clock instead of accumulating local 1-second decrements (no drift).
  // 0 when not running; RemainingSeconds holds the frozen value while paused/idle.
  EndsAt: int64
}

// ─── Drive history ───────────────────────────────────────────────────────────
//
// Append-only log of driving events, written to /sessions/{id}/driveHistory.
// Each drive segment is a `Started` event followed by the end event that closed
// it (`Stopped` | `Skipped` | `Finished` | `Switched`); pair them by driver in
// chronological order to compute per-driver statistics later.

[<RequireQualifiedAccess>]
type DriveEventType =
  | Started
  | Stopped
  | Skipped
  | Finished
  | Switched
  | Paused
  | Resumed

module DriveEventType =
  let toString =
    function
    | DriveEventType.Started -> "Started"
    | DriveEventType.Stopped -> "Stopped"
    | DriveEventType.Skipped -> "Skipped"
    | DriveEventType.Finished -> "Finished"
    | DriveEventType.Switched -> "Switched"
    | DriveEventType.Paused -> "Paused"
    | DriveEventType.Resumed -> "Resumed"

  let fromString (s: string) =
    match s with
    | "Stopped" -> DriveEventType.Stopped
    | "Skipped" -> DriveEventType.Skipped
    | "Finished" -> DriveEventType.Finished
    | "Switched" -> DriveEventType.Switched
    | "Paused" -> DriveEventType.Paused
    | "Resumed" -> DriveEventType.Resumed
    | _ -> DriveEventType.Started

[<CLIMutable>]
type DriveEvent = {
  Type: string // DriveEventType
  Driver: string // driver this event concerns
  By: string // user who triggered the event
  At: int64 // unix ms
}

[<CLIMutable>]
type TodoItemState = { Text: string; Completed: bool }

[<CLIMutable>]
type TodoState = {
  Items: Dictionary<string, TodoItemState>
}
