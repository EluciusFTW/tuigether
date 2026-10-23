using System.Collections.Immutable;
using System.Diagnostics;
using Tuigether.Server.Push;
using Tuigether.Server.Sessions;
using Wolverine;
using Wolverine.Marten;

namespace Tuigether.Server;

/// <summary>
/// The answer of a Command handler to the HTTP endpoint that invoked it.
/// </summary>
public abstract record CommandOutcome
{
    private CommandOutcome() { }

    public sealed record Accepted : CommandOutcome;

    public sealed record Rejected(string Reason) : CommandOutcome;

    public sealed record SessionNotFound : CommandOutcome;
}

/// <summary>
/// Runs a Command on an existing Session. Wolverine retries a message handler after a concurrency conflict, but not an
/// HTTP endpoint. So the endpoint invokes a message handler, and the retry decides the Command again against the new
/// state of the Session.
/// </summary>
public static class SessionCommand
{
    public static async Task<IResult> InvokeAsync(IMessageBus bus, object command, CancellationToken cancellation) =>
        await bus.InvokeAsync<CommandOutcome>(command, cancellation) switch
        {
            CommandOutcome.Accepted => Results.NoContent(),
            CommandOutcome.Rejected { Reason: var reason } => Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: reason),
            CommandOutcome.SessionNotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "The Session does not exist."),
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// Appends the events of an accepted Command and signals the change to every node.
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
