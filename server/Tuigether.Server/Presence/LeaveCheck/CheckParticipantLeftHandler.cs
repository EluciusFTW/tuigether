using Marten;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Push;
using Wolverine.Marten;

namespace Tuigether.Server.Presence.LeaveCheck;

public static class CheckParticipantLeftHandler
{
    // The exclusive lock on the stream makes an open wait for the check, or the check wait for the open. The query of
    // the open Clients runs after the lock, so it sees the Client of an open that committed first.
    // A check can outlive its Session, for example after a delete.
    public static async Task<(Events, BroadcastSessionChanged?)> HandleAsync(
        CheckParticipantLeft check,
        [WriteAggregate(Required = false, LoadStyle = ConcurrencyStyle.Exclusive)] Session? session,
        IDocumentSession documents,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        var hasOpenClient = await documents.Query<ConnectedClient>()
            .AnyAsync(client => client.SessionId == check.SessionId && client.User == check.User, cancellation);
        var events = session is null ? [] : CheckParticipantLeftRules.Leave(session, check.User, hasOpenClient, clock.GetUtcNow());
        return ([.. events], events.IsEmpty ? null : new BroadcastSessionChanged(check.SessionId));
    }
}
