using Marten;
using Tuigether.Contracts;
using Tuigether.Server.Shared.Hubs;
using Wolverine.SignalR;

namespace Tuigether.Server.Sessions.ListView;

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
