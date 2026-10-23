# C#

This doc tells you how C# code in this repository is written. The style is functional: pure functions over immutable
data, with the effects at the edge. Read it before you write, change or review C# code, or add a C# project. The
repository targets .NET 10, so the language is C# 14.

## Gates

- `dotnet build Tuigether.slnx`. Keep it at 0 warnings.
- Before you report work complete: `dotnet format <project> --include <file> ...` on each `.cs` file you changed.

## Critter Stack precedence

This is a hard rule. When a rule in this doc conflicts with the intended use or a documented convention of a Critter
Stack library (Wolverine, Marten, Polecat, Fisher, Bobcat, Alba, Weasel), the library wins. The libraries change
between versions. Before you write a handler, an endpoint, a stored type, a projection or a test, look up the
convention on the library's docs site: [wolverinefx.net](https://wolverinefx.net), [martendb.io](https://martendb.io),
[bobcat.jasperfx.net](https://bobcat.jasperfx.net). When you find a conflict that is not in this list, follow the
library and add the conflict here.

Where to look up:

- Wolverine and Marten publish the whole docs site as one text file: `https://wolverinefx.net/llms-full.txt` and
  `https://martendb.io/llms-full.txt`. `llms.txt` at the same place is the index. Search the file with `grep` instead
  of reading it, because each file has more than 1 MB.
- Bobcat has no such file. The Wolverine file contains the Bobcat vocabulary for the Critter Stack (`WolverineSpec`,
  `WolverineHttpSpec`, `GivenEvents`, `ThenEvents`).
- These files show the current docs, not the pinned package versions. When the behaviour of the pinned version
  matters, read the `docs/` folder of the library repository at the release tag: `V6.51.3` for Wolverine, `v9.51.0`
  for Marten.

Known conflicts:

- **Failure in a handler.** A Wolverine handler or HTTP endpoint never returns a `Result`. Wolverine publishes a
  returned object as a cascading message, and the aggregate handler workflow appends it as an event. Reject in a
  `Validate`, `Before` or `Load` method instead: return `HandlerContinuation.Stop`, or a `ProblemDetails` from an HTTP
  endpoint (`WolverineContinue.NoProblems` continues). Such a method may call a core function that returns a
  `Result`, and match it there.
- **Exceptions in a handler.** Wolverine retries, requeues and dead-letters a message by exception type. Marten throws
  `ConcurrencyException` on a stale write. Let an infrastructure exception leave the handler, and configure a policy
  with `opts.OnException<T>()`. In code that Wolverine calls, this replaces the catch-and-convert rule.
- **Handler shape.** A handler is a `public static class` whose name ends in `Handler`, with a `Handle` or
  `HandleAsync` method. It gets services as method parameters, not through a constructor. It publishes further
  messages as return values (cascading messages), not through `IMessageBus`. The handler is the shell, and a pure
  function makes the business decision.
- **Command (mediator style).** A Wolverine retry policy applies to a message handler, also inline through
  `IMessageBus.InvokeAsync`, but not to an HTTP endpoint. A Command on an existing Session must run again against the
  new state after a concurrency conflict. Its endpoint therefore stays thin: it wraps the Command and the user name into
  a message (`RotateDriverBy` in `Driving/Rotate/`) and calls `SessionCommand.InvokeAsync` (`Shared/Commands/`). The
  message handler uses the aggregate handler workflow (`[WriteAggregate]`) and calls a pure rule. It returns a
  `CommandOutcome` as the reply, and `SessionCommand` turns the outcome into the HTTP answer. `SessionCommand.Decide`
  builds the reply, the events and the broadcast from the rule's `Result`. A Command that starts a stream has no
  concurrency conflict. `CreateSessionHandler` in `Sessions/Create/` uses the same convention all the same, so that
  every Command has one shape. For a Command, this entry replaces "Reject in a `Validate`, `Before` or `Load` method"
  from **Failure in a handler**.
- **Bobcat and an invoked handler.** Bobcat counts the events of a message handler that an endpoint invokes as a
  cascade, so `ThenEvents` and `ThenNoEvents` do not see them. A spec of such a Command checks the stream with
  `ThenEventsOn<Session>(id, version, ...)`, as `DrivingASession` does. `ThenEventsOn` reads an archived stream as
  version 0. A spec of an archived stream therefore queries the events with `MaybeArchived()`, as `DeletingASession`
  does.
- **Broadcast to all nodes.** `BroadcastToAllNodesAsync` has no cascading-message form. Return an `ISideEffect` whose
  `ExecuteAsync(IMessageBus bus)` calls it, as `BroadcastSessionChanged` in `Shared/Push/` does. The endpoint stays a
  pure function, and the outbox holds the broadcast until the transaction commits.
- **Messages from a hub event.** A SignalR hub is not a Wolverine handler, so it has no return value for cascading
  messages. A hub override such as `OnDisconnectedAsync` sends its message through an injected `IMessageBus`, as
  `TuigetherHub` in `Shared/Hubs/` does.
- **Rejection of a SignalR connect.** Throw a `HubException` from `OnConnectedAsync`. SignalR sends its message to
  the client in the close message. An HTTP rejection of the connect reaches the .NET SignalR client only as a status
  code. This rule comes from ASP.NET Core SignalR, because a Wolverine hub derives from its `Hub`.
- **No layer over the libraries.** Use `IDocumentSession` and Wolverine's declarative loading (`[Entity]`,
  `[WriteAggregate]`) in the handler. A repository layer hides the features that Wolverine builds on.
- **Stored types.** Marten stores documents, aggregates and events as JSON through System.Text.Json, its default since
  v8. Use the shapes from the Marten docs: positional records, `init` properties, or `private set` with
  `[JsonInclude]`. A stored type with a private constructor needs `[JsonConstructor]` on it, or the read throws
  `NotSupportedException`.
- **Aggregates.** An immutable aggregate evolves through a static `Create(event)` and static `Apply(event, aggregate)`
  methods that return `aggregate with { ... }`. `with` is the intended update here, so aggregate properties are `init`.
- **Identifiers.** Marten requires a strong-typed ID to be an immutable struct whose name ends in `Id`:
  `public readonly record struct OrderId(Guid Value);`. An ID therefore has no `Create` that returns a `Result`.
  Marten assigns a sequential Guid to an empty Guid ID on store, so leave it empty instead of
  `Guid.CreateVersion7()`.
- **Events and messages.** Marten applies events and Wolverine routes messages by concrete type. An event or a message
  is therefore a standalone public sealed record, not a union case. Marten stores the event class name in snake_case
  without the namespace (`OrderPlaced` becomes `order_placed`), so each event class name is unique in the solution. A
  renamed event class needs `Events.MapEventType<NewName>("old_name")`, or Marten cannot match the stored events to it.
- **SignalR in a module.** `UseSignalR<THub>()` adds the SignalR services to `WolverineOptions.Services`.
  `services.ConfigureWolverine(...)` runs when the container resolves `WolverineOptions`, after the container is built.
  A call to `UseSignalR` there adds nothing, and the hub fails with "Unable to find the required services". So
  `UseSignalR<TuigetherHub>().ProcessInline()` stays in `UseWolverine` in `Program.cs`.
  `PublishMessage<T>().ToSignalR()` changes only the routing, so it works from a module.

## Vertical slices

The server project `server/Tuigether.Server` uses Vertical Slice Architecture. One folder holds one use case.

Folder rules:

- A feature folder (`Sessions/`, `Presence/`, `Driving/`, `Notes/`) holds one subfolder per use case. A use case folder holds the
  endpoint, the message, the handler and the pure rule of that use case: `Driving/Rotate/`. Nest deeper when a use case
  has parts of its own.
- Code that two or more slices use moves up to their lowest common folder. Use no partial classes to spread one type
  over several slices.
- A projection belongs to the slice that serves its read model, not to the features whose events it reads. The events
  live in `Tuigether.Contracts`, so a projection depends on no other feature folder. A read model slice has the suffix
  `View`: `Driving/TimerView/`.
- `Domain/` at the project root holds the aggregate `Session` and the rules that two or more features call, such as
  `SessionRules.CheckNotDeleted`. A rule that one use case calls stays in its slice.
- `Shared/<slug>/` holds boilerplate. Each slug names one purpose: `Shared/Hubs/`, `Shared/Requests/`. A file that
  belongs to no group lies loose in `Shared/`: `Result.cs`, `ServerOptions.cs`.
- The namespace of each file matches its folder.
- A folder name is never the name of a type that the code uses. The folder name becomes a namespace. Inside that
  namespace and its parent, the name then refers to the namespace, and the type does not compile (CS0118). Example:
  the folder `Sessions/CreateSession/` breaks every use of the Command `CreateSession` in `Tuigether.Server.Sessions`.
  So use `Sessions/Create/`, `Driving/Rotate/`, `Shared/Hubs/` (SignalR has a type `Hub`).

Module rules:

- `Module.cs` registers the dependency injection and the library configuration of its folder. A folder gets a
  `Module.cs` only when something needs to be registered.
- A module sits in the folder closest to what it registers. A parent folder gets a `Module.cs` that calls the modules of
  its subfolders. It also registers what belongs to the parent folder itself, as `Presence/Module.cs` does with the
  durable queue of `CheckParticipantLeft`. `Program.cs` calls the top-level modules: `AddDomain()`, `AddSessions()`,
  `AddPresence()`, `AddDriving()`, `AddNotes()` and `AddShared()`.
- The file is always `Module.cs`, and the class is always `public static class Module`. It holds one extension member
  on `IServiceCollection` in an `extension` block, named after the folder path: `AddDrivingTimerView()`. Each folder
  has its own namespace, so the `Module` classes do not collide. A caller uses the extension member, not the class
  name.
- A module adds Marten configuration with `services.ConfigureMarten(...)`, and Wolverine configuration with
  `services.ConfigureWolverine(...)`. See **SignalR in a module** for the exception.
- Each `...View/Module.cs` registers its projection with `ProjectionLifecycle.Inline` and routes its read model type to
  SignalR.
- The request pipeline is not dependency injection, so it gets no module. Each step is an extension member on
  `WebApplication` in its `Shared/<slug>/` folder: `app.UseRequestCheck()`, `app.MapTuigetherHub()`. `Program.cs`
  calls each step itself, in this order: request check, health checks, hub, endpoints. The request check must run
  before the hub and the endpoints.
- `Program.cs` keeps the host-wide settings: the options validation of `ServerOptions`, `TimeProvider`, the health
  checks, the Marten connection, the schema name, the correlation id, `IntegrateWithWolverine`,
  `UseLightweightSessions`, `AddWolverineHttp`, and in `UseWolverine` the service name, the assembly discovery,
  `AutoApplyTransactions`, the durability mode, the polling interval, the `ConcurrencyException` retry and
  `UseSignalR`.

## Specs

- When no Bobcat step covers a value, assert it with Shouldly. Bobcat shows a Shouldly failure as an expected/actual
  cell, with no registration.

## Functional core, imperative shell

- The **core** is pure. A core function computes its result from its parameters only, and it returns a value.
- The **shell** does the effects. It loads state, calls the core, and writes the result. The DI container, the
  database session, `HttpClient` and `TimeProvider` live in the shell.
- Pass time and identity into the core as values: `DateTimeOffset now`, `Guid id`. The shell gets them from
  `TimeProvider.GetUtcNow()` and `Guid.CreateVersion7()`. A core function then returns the same result for the same
  input.
- A core function lives in a `static class` that groups the functions of one concept, as an F# module does:
  `public static class Pricing`.
- Mutation inside one function body is fine: a local `List<T>`, a `foreach` accumulator. The caller sees only the
  immutable result.
- Write the functional style with C# idioms: records, switch expressions, LINQ method syntax, extension members,
  `static` lambdas. A C# reader then reads the code without a library manual.

## Data

- Model data as a `sealed record`. Use a positional record for plain data: a message, an event, a wire DTO.
- Make illegal states unrepresentable. A choice between shapes is a [union](#unions), not a set of nullable fields
  and a flag.
- Parse, don't validate. A type with an invariant gets a private constructor, get-only properties and a static
  `Create` that returns a `Result`. Code that holds an instance trusts it. The shell turns a wire DTO into such a type
  through `Create`.

```csharp
public sealed record EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static Result<EmailAddress, string> Create(string raw) =>
        raw.Contains('@')
            ? Result.Ok(new EmailAddress(raw.Trim()))
            : Result.Error($"'{raw}' is not an email address.");
}
```

Record tripwires:

- `with` copies shallowly and skips the constructor. It runs only the `init` accessors, so a public `init` property
  accepts any value through `with`. This is why a type with an invariant has get-only properties.
- Record equality compares a collection member by reference. `ImmutableArray<T>`, `List<T>` and arrays all do. Two
  records with equal items are therefore not equal. When equality matters (a test assertion, a dictionary key, change
  detection), write `Equals` and `GetHashCode` with `SequenceEqual`.
- `ToString()` prints every property. Override `PrintMembers` on a record that holds a secret, because a log line
  calls `ToString()`.
- `default`, a new array and an uninitialized field skip every constructor. Use a `readonly record struct` only when
  all-zero fields are a legal value. A type with an invariant is a `sealed record`.
- A class primary constructor parameter is a mutable captured field, and an assignment to it gives no warning. Treat
  it as read-only.

## Collections

- Store a collection as `ImmutableArray<T>`, `ImmutableDictionary<TKey, TValue>` or `FrozenDictionary<TKey, TValue>`.
  Build it with a collection expression: `[..items, item]`.
- Accept `IEnumerable<T>` or `IReadOnlyList<T>` as a parameter. Never store one: `IReadOnlyList<T>` is a view, and its
  owner can still change the list.
- `default(ImmutableArray<T>)` is not empty. `IsDefault` is `true`, and enumeration throws `NullReferenceException`.
  An uninitialized field and a missing JSON property both get this value. Declare an `ImmutableArray<T>` member as a
  property with `= []`, not as a positional parameter.

## Unions

C# 14 has no union type. The C# 15 `union` and `closed` keywords need `net11.0`. A union is an abstract record with a
private constructor and sealed nested cases. The private constructor keeps the cases inside the record.

```csharp
public abstract record Payment
{
    private Payment() { }

    public sealed record Card(string Token) : Payment;

    public sealed record Invoice(string Iban, int DueDays) : Payment;
}

static decimal Fee(Payment payment) =>
    payment switch
    {
        Payment.Card => 0.30m,
        Payment.Invoice { DueDays: > 30 } => 2.00m,
        Payment.Invoice => 1.00m,
        _ => throw new UnreachableException(),
    };
```

- The compiler does not know that the case list is closed. Every switch expression on a union therefore ends in
  `_ => throw new UnreachableException()`, and a new case compiles and throws at run time. Before you add a case, run
  LSP `findReferences` on the base type, and add an arm to every switch on it.
- Use an enum only for cases without data. An enum is open: any integer is a valid value. A switch over every named
  member still needs the discard arm.
- A union that crosses the wire needs `[JsonPolymorphic]` on the base and one `[JsonDerivedType(typeof(Case), "case")]`
  per case. A missing case fails at run time, not at build. Stored data keeps the discriminator string, so the string
  stays when the class name changes.
- System.Text.Json reads the `$type` discriminator only as the first property, unless
  `JsonSerializerOptions.AllowOutOfOrderMetadataProperties` is `true`. PostgreSQL `jsonb` reorders keys. JSON that
  comes back from `jsonb` therefore needs the option.

## Failure and absence

- Return an expected failure as `Result<T, TError>`: invalid input, a missing entity, a rejected command. The
  signature then tells the caller to handle it.
- `TError` is a union of the failures of one operation. Use `string` when the caller only shows the failure.
- The solution has one `Result<T, TError>` type, in the shared core project. If it does not exist yet, create it with
  this surface: `Result.Ok(value)` and `Result.Error(error)`, each with an implicit conversion to the target
  `Result<T, TError>`; `Match`, `Map`, `Bind` and `MapError`; and async overloads of these as extension members on
  `Task<Result<T, TError>>`.
- Chain results with `Bind` and `Map` (railway-oriented). End the chain with `Match` in the shell.
- Model absence with a nullable type: `Customer?`, `int?`. Prove a value non-null with a pattern (`is not null`,
  `is { } customer`) instead of the `!` operator.
- `T?` on an unconstrained type parameter is not `Nullable<T>`. For `T = int` it is `int`, and absence reads as `0`.
  Constrain `T` to `class` or `struct`, or return a `Result`.
- Throw only for a bug: the unreachable arm of a switch, a state that the types should prevent.
- The BCL and libraries throw. Catch the documented exception types in the shell, around the call that throws, and
  convert them to a `Result`.
- Let `OperationCanceledException` propagate. A `Result` never holds a cancellation.

## Functions and LINQ

- Compute a value with an expression: a switch expression, `?:`, LINQ. Use statements for the effects in the shell,
  and `if` with an early return for a guard clause.
- Keep each lambda in a LINQ pipeline pure. Do the effects in a `foreach` in the shell.
- Materialize a sequence before it leaves a function: `ToImmutableArray()`, `ToList()`. A deferred `IEnumerable<T>`
  runs the pipeline again at each enumeration, side effects included, and it throws at the enumeration site, far from
  the cause. Return a deferred `IEnumerable<T>` only for a stream that one caller enumerates once.
- `First`, `Single`, `Last`, `Min`, `Max`, `Average` and an unseeded `Aggregate` throw on an empty sequence. Use
  `FirstOrDefault` for a reference type, and give `Aggregate` a seed. For a value type, `FirstOrDefault` returns `0`,
  a legal value: project to the nullable type first, `.Select(x => (int?)x).FirstOrDefault()`.
- Mark a lambda `static` when it needs no captured state. The compiler then rejects an accidental capture. A closure
  captures the variable, not its value, so a later change to the variable shows in the lambda.
- Put combinators and helpers for types that you cannot change into a C# 14 extension block:
  `extension<T, TError>(Result<T, TError> result) { ... }`.
- .NET 10 has async LINQ in the BCL (`System.Linq.AsyncEnumerable`). The `System.Linq.Async` package collides with it
  (error CS0121). Reference no `System.Linq.Async`. If a dependency brings it in, add `System.Linq.Async` 7 or later.

## Async

- An async operation that can fail returns `Task<Result<T, TError>>`.
- An async lambda passed to an `Action` parameter is `async void`. Nothing awaits it, and its exception ends the
  process. `List<T>.ForEach(async x => ...)` is the usual case. Use `foreach` with `await`, or
  `await Task.WhenAll(items.Select(...))`.

## Add a C# project

1. The shared C# MSBuild settings live in one `Directory.Build.props`. A `Directory.Build.props` at the root or in
   `src/` also applies to `src/Tuigether.fsproj`, where `Nullable` and `TreatWarningsAsErrors` change the F# build. So
   put the C# settings into a property group with a condition:

   ```xml
   <PropertyGroup Condition="'$(MSBuildProjectExtension)' == '.csproj'">
     <TargetFramework>net10.0</TargetFramework>
     <Nullable>enable</Nullable>
     <ImplicitUsings>enable</ImplicitUsings>
     <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
     <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
   </PropertyGroup>
   ```

   Leave `LangVersion` unset. `net10.0` selects C# 14.
2. Add a `[*.cs]` section to `.editorconfig` with `dotnet_diagnostic.CA1851.severity = warning`. CA1851 reports a
   multiple enumeration of an `IEnumerable<T>`.
3. Add the project to `Tuigether.slnx`.

Proof: `dotnet build Tuigether.slnx` builds the F# project with the same warnings as before, and the C# project with
0 warnings.
