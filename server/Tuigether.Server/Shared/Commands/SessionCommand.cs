using System.Collections.Immutable;
using System.Diagnostics;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Marten;

namespace Tuigether.Server.Shared.Commands;

/// <summary>
/// The answer of a Command handler to the HTTP endpoint that invoked it.
/// </summary>
public abstract record CommandOutcome
{
    private CommandOutcome() { }

    public sealed record Accepted : CommandOutcome;

    public sealed record Created(Guid SessionId) : CommandOutcome;

    public sealed record Rejected(string Reason) : CommandOutcome;

    public sealed record SessionNotFound : CommandOutcome;
}

/// <summary>
/// Runs a Command through its message handler. Wolverine retries a message handler after a concurrency conflict, but not
/// an HTTP endpoint. The retry decides a Command on an existing Session again against the new state of the Session.
/// </summary>
public static class SessionCommand
{
    public static async Task<IResult> InvokeAsync(IMessageBus bus, object command, CancellationToken cancellation) =>
        await bus.InvokeAsync<CommandOutcome>(command, cancellation) switch
        {
            CommandOutcome.Accepted => Results.NoContent(),
            CommandOutcome.Created { SessionId: var sessionId } => TypedResults.Created(default(string), new CreateSessionResponse(sessionId)),
            CommandOutcome.Rejected { Reason: var reason } => Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: reason),
            CommandOutcome.SessionNotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "The Session does not exist."),
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// The user of the request. <see cref="Requests.RequestCheckMiddleware"/> makes the user name header the user of
    /// the request, and Wolverine relays it to the envelope.
    /// </summary>
    public static string UserOf(Envelope envelope) =>
        envelope.UserName ?? throw new InvalidOperationException("The message has no user name.");

    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) NotFound() =>
        (new CommandOutcome.SessionNotFound(), [], null, []);

    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Reject(string reason) =>
        (new CommandOutcome.Rejected(reason), [], null, []);

    /// <summary>
    /// Appends <paramref name="events"/> to the Session stream, signals the change to every node and sends
    /// <paramref name="messages"/>.
    /// </summary>
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Accept(
        Session session,
        Events events,
        OutgoingMessages? messages = null) =>
        (new CommandOutcome.Accepted(), events, new BroadcastSessionChanged(session.Id), messages ?? []);

    /// <summary>
    /// Appends the events of an accepted Command on an existing Session and signals the change to every node.
    /// </summary>
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Decide(
        Session? session,
        Func<Session, Result<ImmutableArray<object>, string>> rule) =>
        session is null
            ? (new CommandOutcome.SessionNotFound(), [], null)
            : rule(session).Match<(CommandOutcome, Events, BroadcastSessionChanged?)>(
                events => (new CommandOutcome.Accepted(), [.. events], new BroadcastSessionChanged(session.Id)),
                static reason => (new CommandOutcome.Rejected(reason), [], null));
}
