module Backend

open System.Collections.Generic
open Elmish

type SessionEvent =
  // Connected users per Session id ride along with the Session list, so the list needs no per-Session presence
  // subscription.
  | SessionsLoaded of sessions: (string * Session.Data) list * connectedUsers: Map<string, Set<string>>
  | SessionChanged of string * Session.Data * connectedUsers: Set<string>
  | SessionRemoved of string
  | ConnectionError of string

// One event per User, so a consumer keeps its own order of the Participants. A backend whose source pushes a whole
// Participants snapshot diffs it against the previous snapshot and dispatches these events.
type UserEvent =
  | UserChanged of user: string * presence: Session.UserPresence
  | UserRemoved of user: string

type TimerPhase =
  // A backend that cannot tell a paused Drive from an idle timer sends Stopped.
  | Stopped
  | Idle
  | DriveRunning
  | DrivePaused
  // The Break for all Participants. A backend without a shared Break never sends it, and each Client runs its own
  // Break.
  | Break

// The seam's own timer type, apart from the wire record `Session.TimerState`.
type TimerSnapshot = {
  Phase: TimerPhase
  RemainingSeconds: int
  // Unix ms when the running Drive or the Break ends. 0 when neither runs.
  EndsAt: int64
}

type ConnectionState =
  | Connecting
  | Connected
  // The connection broke or never came up.
  | Disconnected
  // The server refused the Client, for example for a wrong API key.
  | Refused of reason: string

type IConnection =
  // A backend without a connection state of its own sends nothing.
  abstract Subscription: Sub<ConnectionState>

type ISessions =
  abstract Subscription: Sub<SessionEvent>

  abstract Create:
    user: string -> title: string -> gitBranch: string -> gitRepo: string -> Async<Result<string, string>>

  abstract Delete: sessionId: string -> Async<Result<unit, string>>
  // Sets a Created Session to Started, after the first successful Join.
  abstract Start: sessionId: string -> Async<unit>
  abstract DataSubscription: sessionId: string -> Sub<Session.Data option>

type IPresence =
  abstract Join: sessionId: string -> user: string -> avatarName: string -> Async<Result<unit, string>>
  // Also pauses the Drive when the Driver leaves, and finishes a Started Session when its last Participant leaves.
  // On quit, the app exits when the returned Async completes, so every write completes inside it.
  abstract Leave: sessionId: string -> user: string -> wasStarted: bool -> wasDriver: bool -> Async<unit>
  // `subscriberTag` gives each consumer its own subscription key.
  abstract Subscription: sessionId: string -> subscriberTag: string -> Sub<UserEvent>

// `by` is the User who acts. `driver` is the Driver whose Drive the intention concerns; without one, a backend writes
// no history entry. `proposed` is the timer state that the Client computed.
type IDriving =
  abstract Subscription: sessionId: string -> Sub<TimerSnapshot option>

  // `endsCurrentDrive`: the current Driver has a running or paused Drive. `proposed` starts the next Drive.
  abstract RotateDriver:
    sessionId: string ->
    by: string ->
    currentDriver: string option ->
    endsCurrentDrive: bool ->
    nextDriver: string option ->
    proposed: TimerSnapshot ->
      Async<unit>

  abstract PauseDrive:
    sessionId: string -> by: string -> driver: string option -> proposed: TimerSnapshot -> Async<unit>

  abstract ResumeDrive:
    sessionId: string -> by: string -> driver: string option -> proposed: TimerSnapshot -> Async<unit>

  abstract SkipDrive: sessionId: string -> by: string -> driver: string option -> proposed: TimerSnapshot -> Async<unit>
  // Every Client sends it when the Drive time runs out.
  abstract EndDrive: sessionId: string -> proposed: TimerSnapshot -> Async<unit>
  // Only the Driver's Client sends it when the Drive time runs out, so the history gets one entry.
  abstract RecordFinishedDrive: sessionId: string -> by: string -> driver: string -> Async<unit>
  // Every Client sends it when the Break time runs out.
  abstract EndBreak: sessionId: string -> proposed: TimerSnapshot -> Async<unit>
  abstract SkipBreak: sessionId: string -> proposed: TimerSnapshot -> Async<unit>
  // The Driver leaves the Session.
  abstract ClearDriver: sessionId: string -> Async<unit>

type IDriveHistory =
  // Keyed by an id that sorts in time order.
  abstract Subscription: sessionId: string -> Sub<Dictionary<string, Session.DriveEvent> option>

// The Edit lock of one text. The Client proposes the lock with its own clock; `Locking` holds the time limit.
type IEditing =
  // Ok accepts the proposed lock. `current` is the lock that the Client shows. A backend that checks the Edit lock
  // itself ignores it.
  abstract AcquireEditLock:
    sessionId: string -> lock: Locking.Lock -> current: Locking.Lock option -> Async<Result<unit, string>>
  // Live typing: the debounced save while the editor is open. It also writes the refreshed lock.
  abstract SaveDraft: sessionId: string -> text: string -> lock: Locking.Lock option -> Async<unit>
  // Saves the final text and releases the Edit lock.
  abstract EndEdit: sessionId: string -> text: string -> Async<unit>

type INotes =
  inherit IEditing
  abstract Subscription: sessionId: string -> Sub<Session.NotesState option>

// The Goal arrives with the Session data, see `ISessions.DataSubscription`.
type IGoal =
  inherit IEditing

type ITodo =
  abstract Subscription: sessionId: string -> Sub<Session.TodoState option>
  abstract Add: sessionId: string -> itemId: string -> text: string -> Async<unit>
  // Carries the whole item for a backend that writes the text and the state together.
  abstract ChangeText: sessionId: string -> itemId: string -> item: Session.TodoItemState -> Async<unit>
  abstract Complete: sessionId: string -> itemId: string -> Async<unit>
  abstract Reopen: sessionId: string -> itemId: string -> Async<unit>

  // `swapped` holds two neighbour items with their content swapped, for a backend that orders the items by key. A
  // backend that stores positions moves `itemId` to `newPosition`.
  abstract Move:
    sessionId: string ->
    itemId: string ->
    newPosition: int ->
    swapped: (string * Session.TodoItemState) * (string * Session.TodoItemState) ->
      Async<unit>

  abstract Remove: sessionId: string -> itemId: string -> Async<unit>

type INoteList =
  abstract Subscription: sessionId: string -> Sub<Session.ListState option>
  abstract Add: sessionId: string -> itemId: string -> text: string -> Async<unit>
  abstract Change: sessionId: string -> itemId: string -> text: string -> Async<unit>
  abstract Remove: sessionId: string -> itemId: string -> Async<unit>

type IGit =
  abstract ChangeBranch: sessionId: string -> branch: string -> Async<unit>
  abstract RecordWipPush: sessionId: string -> user: string -> pushedAt: int64 -> Async<unit>

// One group per area. Each function is one user intention.
type Backend = {
  Connection: IConnection
  Sessions: ISessions
  Presence: IPresence
  Driving: IDriving
  DriveHistory: IDriveHistory
  Notes: INotes
  Goal: IGoal
  Todo: ITodo
  NoteList: INoteList
  Git: IGit
}
