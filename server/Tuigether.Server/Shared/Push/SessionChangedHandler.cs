using Marten;
using Tuigether.Contracts;
using Tuigether.Server.Shared.Hubs;
using Wolverine.SignalR;
using Wolverine.SignalR.Internals;

namespace Tuigether.Server.Shared.Push;

public static class SessionChangedHandler
{
    public static async Task<(SessionSummary?, SessionSnapshots)> LoadAsync(
        SessionChanged changed,
        IQuerySession query,
        CancellationToken cancellation) =>
        (
            await query.LoadAsync<SessionSummary>(changed.SessionId, cancellation),
            await SessionSnapshots.LoadAsync(query, changed.SessionId, cancellation));

    // The summary is null for a deleted Session, because the projection deletes it.
    public static IEnumerable<object> Handle(SessionChanged changed, SessionSummary? summary, SessionSnapshots snapshots) =>
        [
            summary is not null
                ? summary.ToWebSocketGroup(TuigetherHub.SessionListGroup)
                : new SessionRemoved(changed.SessionId).ToWebSocketGroup(TuigetherHub.SessionListGroup),
            .. snapshots.MessagesTo(new WebSocketRouting.Group(TuigetherHub.SessionGroup(changed.SessionId))),
        ];
}
