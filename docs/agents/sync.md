# Sync

This doc tells you how tuigether clients share state through a sync backend (Firebase or the sync server), and why the sync tripwires in
[`CLAUDE.md`](../../CLAUDE.md) exist. Read it before you change `Backend.fs`, `Firebase.fs`, `Session.fs`, a lock, or the timer.

## Where it lives

- `Firebase.fs` owns every database path, read, write and subscription, in one submodule per area (`Sessions`,
  `Users`, `Notes`, `NoteList`, `Todo`, `Timer`, `History`). No other file builds a query.
- `Backend.fs` is the seam between the components and a sync backend. `FirebaseBackend.fs` implements it with
  `Firebase.fs`, and it is the only file that calls `Firebase.fs`. `ServerBackend.fs` implements it with the sync
  server in `server/`. See [Backend seam](#backend-seam) and [Server backend](#server-backend).
- `Session.fs` holds the wire records.
- Timestamps are Unix milliseconds from `Clock.nowMs ()`.

## Wire format and mixed versions

Each user installs their own binary with `scripts/install.sh` or `scripts/install.ps1`. One session therefore often
has clients on different versions, and every record must stay readable in both directions.

- The `Session.fs` records are `[<CLIMutable>]`, and Newtonsoft.Json serializes them. A field name is a JSON key in
  the database.
- Many writes address one field by a string literal that equals the record field name, for example
  `node.Child "GitBranch"` in `Firebase.Sessions`. A rename must change the literal too, and existing data still has
  the old key. A renamed field therefore reads as missing on every client.
- An old client ignores a field that it does not know. A new client reads a missing field as the default: `null` for
  a string, `0` for a number. Check a new string field with `isNull`, as `SessionInfo.fs` does.
- A whole-record write (`Firebase.Timer.save` writes the full `TimerState`) contains only the fields that the writer
  knows. An old client therefore deletes a new field on its next save. Put a new field where clients write it one
  field at a time, or accept that it resets.
- Newtonsoft writes an F# union as `{"Case": …}`. Union-valued fields therefore stay `string`, and the union's
  `toString`/`fromString` convert them (`Session.Status`, `Session.DriveEventType`). `fromString` maps an unknown
  string to a default case, so an old client reads a new case as that default instead of failing.

## Subscriptions

- Most areas use `subscribeReload`. Any child event under the path triggers a full reload through the area's
  `load`, and one initial load runs on registration. A path with mixed children cannot deserialize per-child events
  into one record, so this is the reliable form.
- `Firebase.Users` uses a typed `AsObservable` stream, because each child is one `UserPresence`.
- The `Sessions` subscription watches all of `/sessions`. An edit in any session reloads the whole session list.

## Writes

- Widget writes are fire-and-forget. `Firebase.swallow` drops a failure, and the next change event re-syncs the
  state. A function returns `Result` only when its caller acts on the failure: `Sessions.create`, `Sessions.delete`,
  `Users.join`, `Users.leave`.
- On quit, the app exits only after `Presence.Leave` completes, so the leave writes finish first.
- List and todo items use keys from `PushId.generate`. The keys sort by creation time, and concurrent clients do not
  collide. `TodoList` reorders items by swapping content between two fixed keys.

## Locks and the timer

- The goal and the freetext notes allow one writer at a time. `Locking.fs` holds the time rules: a lock expires 60 s
  after its last refresh, and each debounced save refreshes it. So the lock of a crashed client expires.
- `TimerState.EndsAt` is the absolute instant when a running drive ends. Every client derives the remaining time from
  its own clock and `EndsAt`. `RemainingSeconds` holds the frozen value while the timer is paused or idle.
- `Backend.TimerSnapshot` is the seam's own timer type. Its `Phase` is `Stopped` from Firebase, because a Firebase
  timer cannot tell a paused Drive from an idle timer. Only a server sends `Break`.

## Backend seam

`Backend.fs` is the seam between the components and a sync backend. `Dependencies.Backend` carries it.

- Each group of the seam covers one area. Each function is one user intention. No function names a database path.
- The groups are `Sessions`, `Presence`, `Driving`, `DriveHistory`, `Notes`, `Goal`, `Todo`, `NoteList` and `Git`.
- A subscription returns a `Sub` of the area's snapshot or event. The consumer maps it to its own message with
  `Subs.map`. `Presence.Subscription` takes a subscriber tag, so each consumer keeps its own subscription key.
- The Client proposes values: the item id, the timer state (`TimerSnapshot`) and the Edit lock. A backend that owns
  the rule can ignore the proposal.
- `SessionView` calls `Sessions.Start` after a successful `Presence.Join` when it sees a Created Session.
  `Presence.Leave` also pauses the Drive when the Driver leaves, and sets a Started Session to Finished when the
  last Participant leaves. The Firebase backend runs these steps in the Client. Each step writes from a value that
  the Client read before, so two Clients can race.

### Presence events

The presence subscription dispatches one `UserEvent` per User: `UserChanged` or `UserRemoved`. Firebase sends this
shape. `Journey` keeps the Participants in the order of the events, and it rotates the Driver in that order.

A backend that receives one Participants snapshot per change converts it inside its own subscription. It diffs each
snapshot against the previous one. It dispatches `UserChanged` for each new or changed User, in snapshot order, and
`UserRemoved` for each missing User. The components therefore stay the same for each backend.

### Intentions that a server can own

Some intentions have an effect that a server backend decides itself. Each of them is its own seam function, so that a
server backend can make it a no-op: `Driving.EndDrive`, `Driving.RecordFinishedDrive`, `Driving.EndBreak` and
`Driving.ClearDriver`. `Backend.fs` describes when a Client sends each one.

The Firebase backend writes each of them. Last write wins, the Break runs on each Client only, and the Edit lock
expires by the time limit in `Locking.fs`.

### Edit lock and live typing

`Notes` and `Goal` share `IEditing`. `AcquireEditLock` sends the proposed lock when Insert mode starts. `SaveDraft`
is the debounced save while the User types, and it refreshes the lock. `EndEdit` sends the final text when Insert mode
ends, by Esc or by the idle auto-exit, and it releases the lock. A backend without live typing makes `SaveDraft` a
no-op.

## Server backend

`ServerBackend.fs` implements the seam against one node of the sync server. `Config.BackendSettings` selects it.

- It sends each Command as an HTTP POST to the route in `Tuigether.Contracts.CommandRoutes`. Each request carries the
  API key, the user name and the protocol version (`RequestHeaders`).
- It receives snapshots over one SignalR connection for the whole app. Each message is a CloudEvents envelope, see
  `HubProtocol.ReceiveMessage`.
- It retries the first connect and every reconnect without limit. After each connect it joins the Session list group
  again, and the node answers with the full Session list.
- A refusal of the connect (wrong API key, other protocol version) is final. The backend stops retrying.
- `IConnection` reports the connection state. The app shows it in the footer. The Firebase backend reports none.
- The backend shows each rejected Command and each refusal as a notification with the reason of the server. The
  components change their model only from snapshots.
- `Presence.Join` sends `OpenSession` with the avatar. The node answers with all snapshots of the Session. The backend
  remembers the open Session and sends `OpenSession` again after each connect. `Presence.Leave` sends `CloseSession`
  and waits for the node.
- The server appends the join, the leave, `SessionStarted` and `SessionFinished` itself. `Sessions.Start` is a no-op,
  and `Presence.Leave` writes nothing else. A User leaves after the grace period of the server (`LeaveGracePeriod`,
  default 10 seconds), when no other Client of the User has the Session open.
- `Driving.RotateDriver`, `PauseDrive` and `ResumeDrive` send their Commands. The server sets `EndsAt` from its own
  clock. The proposed timer gives only the Drive duration. `RotateDriver` carries the Driver that the Client shows, so
  the server rejects the second of two rotations at the same moment.
- When the Driver leaves, the server pauses a running Drive and clears the Driver. `Driving.ClearDriver` is a no-op.
- `Driving.SkipDrive` and `Driving.SkipBreak` send their Commands. The server ends each Drive and each Break at its
  `EndsAt`, starts the Break and writes the Drive history. `Driving.EndDrive`, `Driving.RecordFinishedDrive` and
  `Driving.EndBreak` are no-ops.
- The timer subscription sends each phase of the server: `Idle`, `DriveRunning`, `DrivePaused` and `Break`. The timer
  component counts a `Break` down from its `EndsAt`. It leaves the Break when `Idle` or `DriveRunning` arrives. An
  idle timer ignores `Idle` and keeps its own display.
- The timer component knows a shared Break by its `EndsAt`. Only during a shared Break does `f` skip the Break, and
  does a remote Drive start end the Break. The Firebase Break keeps its behaviour.
- The Driver arrives in `SessionData`. The node sends the Participants before the Session data, because `Journey`
  shows a Driver only when the Driver is in its Participants.
- The backend keeps the last message of each type. A subscription that starts after a push gets that message at once.
- An area that the server does not serve yet is a stub: its subscription sends nothing, and its intentions do nothing.
