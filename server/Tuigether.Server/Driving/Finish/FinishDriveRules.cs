using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Driving.Finish;

public static class FinishDriveRules
{
    /// <summary>
    /// The events at <paramref name="endsAt"/>, the end that a <see cref="FinishDrive"/> was scheduled for. The Break
    /// starts at the end of the Drive, so all Clients count it from the same time.
    /// </summary>
    public static ImmutableArray<object> Finish(Session session, DateTimeOffset endsAt, TimeSpan breakDuration) =>
        session switch
        {
            { IsDeleted: true } => [],
            { Timer: TimerPhase.DriveRunning } when session.DriveEndsAt == endsAt =>
                [
                    new DriveFinished(session.Id, endsAt),
                    DrivingRules.StartBreak(session, user: null, endsAt, breakDuration),
                ],
            _ => [],
        };
}
