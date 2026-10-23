---
status: accepted
---

# Domain events record intentions, in one stream per Session

The sync server is event-sourced with Marten. One stream per Session holds all its domain events. A domain event
records one user intention that the server accepted, or one rule that the server applied. An Elmish `Msg` never
becomes a domain event by itself. One intention is one Command and one atomic append, even when it produces several
events: `RotateDriver` appends `DriveSwitched`, `DriverChanged` and `DriveStarted` together.

## Consequences

- In server mode, the other Participants see Notes and Goal text only when the editor leaves Insert mode. The
  debounced saves during typing are UI work and are not sent. Firebase mode keeps the live view.
- The Break is shared in server mode and stays local in Firebase mode.
- A later split into several streams per Session can apply to new Sessions only, because a Session lasts hours.
