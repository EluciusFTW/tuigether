using Marten;
using Tuigether.Contracts;
using Tuigether.Server.Push;
using Wolverine.SignalR;

namespace Tuigether.Server.Sessions;

public static class JoinSessionListHandler
{
    public static Task<IReadOnlyList<SessionSummary>> LoadAsync(JoinSessionList _, IQuerySession query, CancellationToken cancellation) =>
        query.Query<SessionSummary>().ToListAsync(cancellation);

    public static (AddConnectionToGroup, ResponseToCallingWebSocket<SessionList>) Handle(
        JoinSessionList _,
        IReadOnlyList<SessionSummary> sessions) =>
        (
            new AddConnectionToGroup(TuigetherHub.SessionListGroup),
            new SessionList { Sessions = [.. sessions] }.RespondToCallingWebSocket());
}

public static class SessionChangedHandler
{
    public static Task<SessionSummary?> LoadAsync(SessionChanged changed, IQuerySession query, CancellationToken cancellation) =>
        query.LoadAsync<SessionSummary>(changed.SessionId, cancellation);

    // The summary is null for a deleted Session, because the projection deletes it.
    public static object Handle(SessionChanged changed, SessionSummary? summary) =>
        summary is not null
            ? summary.ToWebSocketGroup(TuigetherHub.SessionListGroup)
            : new SessionRemoved(changed.SessionId).ToWebSocketGroup(TuigetherHub.SessionListGroup);
}
