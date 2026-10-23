# tuigether

tuigether is a terminal app for remote pair and mob programming. Each client syncs one session — driver timer,
notes, todos, git branch — through a shared Firebase Realtime Database. [`src/README.md`](src/README.md) describes
the architecture: the MVU loop, the message sources, the model tree, and the git integration.

## Stack

- F# on .NET 10, one project: `src/Tuigether.fsproj`. F# compiles files in `<Compile Include>` order. Put a new
  file into that list above its first consumer.
- UI: SpectreTuff on Spectre.Tui. Both are early-stage and change between versions. For the API, read the package
  README at `~/.nuget/packages/eluciusftw.spectretuff/<version>/README.md`, or the source at
  <https://github.com/EluciusFTW/SpectreTuff>.
- Elmish 5, run by `SpectreTuff.Elmish`. A subscription is a keyed list,
  `(string list * (dispatch -> IDisposable)) list`. `Subs.map` re-tags a child's list for its parent.
- Database client: the `FirebaseDatabase.net` package (namespace `Firebase.Database`, serializer Newtonsoft.Json).
  The Google `FirebaseAdmin` SDK is a different library.
- Effects: `async { }` with `Cmd.OfAsync.perform`.
- Sync server: C# with Wolverine and Marten on PostgreSQL, in `server/`. `Tuigether.Contracts` holds the wire types,
  `Tuigether.Server` the server, and `Tuigether.Server.Specs` the Bobcat specs.

## Gates

Run every command from the repository root.

- After each change: `dotnet build src`. It takes a few seconds. Keep it at 0 warnings.
- Before you report work complete: build, then run `dotnet fantomas <file> ...` on each `.fs` file you changed (after
  `dotnet tool restore` on a fresh clone). Some files on `main` are not formatted, so `dotnet fantomas src` puts
  their changes into your diff.
- There are no automated tests. A green build proves the types, not the behaviour. Report a behaviour change as
  "builds, not run", and name the keys a human presses to check it.
- Server, after each change: `dotnet build Tuigether.slnx`. It builds the F# client too. Keep it at 0 warnings.
- Server, before you report work complete: `dotnet test --project server/Tuigether.Server.Specs`. It needs Docker,
  because Testcontainers starts PostgreSQL. Then `dotnet format server/<project> --include <file> ...` on each `.cs`
  file you changed.
- Server, after a change to the startup, the configuration or the Dockerfile: `docker compose up --build --detach`.
  `curl localhost:5101/health` and `curl localhost:5102/health` then answer `Healthy`. `docker compose down` stops the
  nodes and deletes the database.
- `dotnet run --project src` starts the full-screen app. It connects to the database in
  `~/.config/tuigether/config.json` or the `FIREBASE_*` variables. Every user of that database sees the sessions it
  creates. Its git actions run in the current directory.

## Exchange folder

`.scratch/` at the repository root is the exchange folder between the human and the agent: plans, handoffs,
research, review notes. Write such files there. `.git/info/exclude` excludes it, so search tools can skip it: list it
with `ls .scratch`.

## Reference

- **Components** — add or change a panel, its key bindings, or its wiring into `SessionView`:
  [`docs/agents/components.md`](docs/agents/components.md).
- **Sync** — change anything that clients share: `Firebase.fs`, `Session.fs`, a lock, the timer:
  [`docs/agents/sync.md`](docs/agents/sync.md).
- **C#** — write, change or review C# code, add a C# project, or use a Critter Stack library:
  [`docs/agents/csharp.md`](docs/agents/csharp.md).

## Tripwires

- The records in `Session.fs` are the database wire format, and clients on older versions read them. Keep each field
  name. Keep union-valued fields as `string` with `toString`/`fromString`. Add a field instead of renaming one.
  Reasons: [`docs/agents/sync.md`](docs/agents/sync.md).
- An Elmish subscription key belongs to one consumer. Elmish keeps the first start function for a key, so two
  consumers with one key route all events to the first. Give each consumer its own key, as `subscriberTag` does in
  `Firebase.Users.subscription`.
- The app ships for Linux, macOS and Windows. OS-specific code handles all three, as `Notification.fs` and
  `Clipboard.fs` do.

## F# conventions

- Branch on a `bool` with `match … with | true -> … | false -> …`. The code uses this form almost everywhere.
- Return failures as `Result<_, string>`. No code in `src` raises an exception.
