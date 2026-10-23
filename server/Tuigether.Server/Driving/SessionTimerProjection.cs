using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Driving;

public sealed partial class SessionTimerProjection : SingleStreamProjection<SessionTimer, Guid>
{
    public static SessionTimer Create(SessionCreated created) =>
        new(created.SessionId, TimerPhase.Idle, Driver: null, EndsAt: null, TimeSpan.Zero);

    public static SessionTimer Apply(DriverChanged changed, SessionTimer timer) => timer with { Driver = changed.Driver };

    public static SessionTimer Apply(DriveStarted started, SessionTimer timer) =>
        timer with { Phase = TimerPhase.DriveRunning, EndsAt = started.EndsAt, Remaining = started.EndsAt - started.At };

    public static SessionTimer Apply(DrivePaused paused, SessionTimer timer) =>
        timer with { Phase = TimerPhase.DrivePaused, EndsAt = null, Remaining = paused.Remaining };

    public static SessionTimer Apply(DriveResumed resumed, SessionTimer timer) =>
        timer with { Phase = TimerPhase.DriveRunning, EndsAt = resumed.EndsAt, Remaining = resumed.EndsAt - resumed.At };

    public static bool ShouldDelete(SessionDeleted _) => true;
}
