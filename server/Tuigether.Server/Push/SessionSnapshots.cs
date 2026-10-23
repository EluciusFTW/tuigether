using Marten;
using Tuigether.Contracts;
using Wolverine.SignalR;
using Wolverine.SignalR.Internals;

namespace Tuigether.Server.Push;

/// <summary>
/// The snapshots that a Client with an open Session receives. Each area adds its snapshot here.
/// </summary>
public sealed record SessionSnapshots(
    SessionParticipants? Participants,
    SessionData? Data,
    SessionTimer? Timer,
    SessionDriveHistory? DriveHistory)
{
    public static async Task<SessionSnapshots> LoadAsync(IQuerySession query, Guid sessionId, CancellationToken cancellation)
    {
        var batch = query.CreateBatchQuery();
        var participants = batch.Load<SessionParticipants>(sessionId);
        var data = batch.Load<SessionData>(sessionId);
        var timer = batch.Load<SessionTimer>(sessionId);
        var driveHistory = batch.Load<SessionDriveHistory>(sessionId);
        await batch.Execute(cancellation);
        return new(await participants, await data, await timer, await driveHistory);
    }

    /// <summary>
    /// One SignalR message per snapshot that exists. A deleted Session has none. The Participants come before the
    /// Session data, because the Client shows the Driver only when the Driver is in its Participants.
    /// </summary>
    public IEnumerable<object> MessagesTo(WebSocketRouting.ILocator clients) =>
        new object?[]
        {
            Participants is null ? null : new SignalRMessage<SessionParticipants>(Participants, clients),
            Data is null ? null : new SignalRMessage<SessionData>(Data, clients),
            Timer is null ? null : new SignalRMessage<SessionTimer>(Timer, clients),
            DriveHistory is null ? null : new SignalRMessage<SessionDriveHistory>(DriveHistory, clients),
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
