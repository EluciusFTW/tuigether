using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Sessions;

public sealed partial class SessionSummaryProjection : SingleStreamProjection<SessionSummary, Guid>
{
    public static SessionSummary Create(SessionCreated created) =>
        new(created.SessionId, created.Title, SessionStatus.Created, created.User, created.Repository, created.Branch, created.At);

    public static bool ShouldDelete(SessionDeleted _) => true;
}
