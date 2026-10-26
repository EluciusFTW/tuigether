using Marten;
using Microsoft.Extensions.Options;
using Tuigether.Server.Shared;
using Wolverine;

namespace Tuigether.Server.Presence.Sweep;

/// <summary>
/// Wolverine publishes it on the schedule in <c>Program.cs</c>.
/// </summary>
public sealed record SweepCrashedNodes;

public static class SweepCrashedNodesHandler
{
    // A crashed node closes none of its Clients. The leave checks run after this transaction commits, so they see the
    // open Clients without the removed ones, and they apply the same rules as after a clean close.
    public static async Task<OutgoingMessages> HandleAsync(
        SweepCrashedNodes _,
        IDocumentSession documents,
        IOptions<ServerOptions> options,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        var stale = clock.GetUtcNow() - options.Value.HeartbeatTimeout;
        var crashed = await documents.Query<NodeHeartbeat>().Where(beat => beat.At < stale).Select(static beat => beat.Id).ToListAsync(cancellation);
        var clients = await documents.Query<ConnectedClient>().Where(client => crashed.Contains(client.NodeId)).ToListAsync(cancellation);

        foreach (var client in clients)
        {
            documents.Delete(client);
        }

        documents.DeleteWhere<NodeHeartbeat>(beat => beat.At < stale);
        return [.. clients.Select(static client => new CheckParticipantLeft(client.SessionId, client.User)).Distinct()];
    }
}
