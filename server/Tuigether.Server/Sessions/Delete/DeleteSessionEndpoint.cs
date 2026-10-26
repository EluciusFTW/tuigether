using JasperFx.Events;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Sessions.Delete;

/// <summary>
/// Invokes <see cref="DeleteSessionHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class DeleteSessionEndpoint
{
    [WolverinePost(CommandRoutes.DeleteSession)]
    public static Task<IResult> Post(DeleteSession command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class DeleteSessionHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        DeleteSession command,
        [WriteAggregate(Required = false)] Session? session,
        Envelope envelope,
        TimeProvider clock)
    {
        if (session is null)
        {
            return SessionCommand.NotFound();
        }

        if (session.IsDeleted)
        {
            return SessionCommand.Reject(SessionRules.DeletedReason);
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();

        // The Archived event makes Marten archive the stream in the same transaction.
        return SessionCommand.Accept(
            session,
            [new SessionDeleted(session.Id, user, now), new Archived("Session deleted")],
            [new MaskDeletedSession(session.Id)]);
    }
}
