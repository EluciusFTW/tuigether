using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Sessions.DataView;

public sealed partial class SessionDataProjection : SingleStreamProjection<SessionData, Guid>
{
    public static SessionData Create(SessionCreated created) =>
        new(created.SessionId, created.Title, SessionStatus.Created, created.User, created.Repository, created.Branch, created.At, Driver: null);

    public static SessionData Apply(SessionStarted _, SessionData data) => data with { Status = SessionStatus.Started };

    public static SessionData Apply(SessionFinished _, SessionData data) => data with { Status = SessionStatus.Finished };

    public static SessionData Apply(DriverChanged changed, SessionData data) => data with { Driver = changed.Driver };

    public static bool ShouldDelete(SessionDeleted _) => true;
}
