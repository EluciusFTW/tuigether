---
status: accepted
---

# Server backend next to Firebase: the client proposes, the server checks

tuigether syncs a Session through Firebase. We add a second sync backend: a .NET server with Wolverine and Marten on
PostgreSQL. Each install selects one backend in its config, and a Session lives in one backend only. The client
components keep their logic and send Commands with proposed values. The server rejects a Command that breaks a Session
invariant, for example a second Edit lock. The server also owns the rules that no single Client can own: the timed
end of a Drive or a Break, presence, and the Session status. Firebase behaviour does not change.

## Considered Options

- **Rules in both backends.** The components send only intents, and each backend applies all rules. The Firebase
  rules run as F# in the client and the server rules run as C# (see ADR 0002), so the two copies drift.
- **Thin server.** The server stores documents shaped like the Firebase tree and broadcasts them. It keeps the
  Firebase races: lock acquisition, `pauseIfRunning` and the last-leave check read and then write.

## Consequences

- The two backends act differently under concurrency. Firebase stays last-write-wins. The server rejects conflicts.
- The seam between components and backends has one function per user intention, not per database path.
