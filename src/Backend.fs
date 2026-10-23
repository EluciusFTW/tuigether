module Backend

open Elmish

type SessionEvent =
  // Connected users per Session id ride along with the Session list, so the list needs no per-Session presence
  // subscription.
  | SessionsLoaded of sessions: (string * Session.Data) list * connectedUsers: Map<string, Set<string>>
  | SessionChanged of string * Session.Data
  | SessionRemoved of string
  | ConnectionError of string

// One event per User, so a consumer keeps its own order of the Participants. A backend whose source pushes a whole
// Participants snapshot diffs it against the previous snapshot and dispatches these events.
type UserEvent =
  | UserChanged of user: string * presence: Session.UserPresence
  | UserRemoved of user: string

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

// One group per area. Each function is one user intention.
type Backend = {
  Sessions: ISessions
  Presence: IPresence
}
