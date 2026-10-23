using Marten;
using Tuigether.Contracts;
using Wolverine.SignalR;
using Wolverine.SignalR.Internals;

namespace Tuigether.Server.Push;

/// <summary>
/// The snapshots that a Client with an open Session receives. Each area adds its snapshot here.
/// </summary>
public sealed record SessionSnapshots(SessionData? Data, SessionParticipants? Participants)
{
    public static async Task<SessionSnapshots> LoadAsync(IQuerySession query, Guid sessionId, CancellationToken cancellation)
    {
        var batch = query.CreateBatchQuery();
        var data = batch.Load<SessionData>(sessionId);
        var participants = batch.Load<SessionParticipants>(sessionId);
        await batch.Execute(cancellation);
        return new(await data, await participants);
    }

    /// <summary>
    /// One SignalR message per snapshot that exists. A deleted Session has none.
    /// </summary>
    public IEnumerable<object> MessagesTo(WebSocketRouting.ILocator clients) =>
        new object?[]
        {
            Data is null ? null : new SignalRMessage<SessionData>(Data, clients),
            Participants is null ? null : new SignalRMessage<SessionParticipants>(Participants, clients),
        }.OfType<object>().ToList();
}

/// <summary>
/// Sends all snapshots of a Session to one connection of this node. It runs after the open commits, so the snapshots
/// contain the join.
/// </summary>
public sealed record SendSessionSnapshots(Guid SessionId, string ConnectionId);

public static class SendSessionSnapshotsHandler
{
    public static Task<SessionSnapshots> LoadAsync(SendSessionSnapshots send, IQuerySession query, CancellationToken cancellation) =>
        SessionSnapshots.LoadAsync(query, send.SessionId, cancellation);

    public static IEnumerable<object> Handle(SendSessionSnapshots send, SessionSnapshots snapshots) =>
        snapshots.MessagesTo(new WebSocketRouting.Connection(send.ConnectionId));
}
