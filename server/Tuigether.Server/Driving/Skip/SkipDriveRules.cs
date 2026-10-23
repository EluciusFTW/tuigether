using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Driving.Skip;

public static class SkipDriveRules
{
    public static Result<ImmutableArray<object>, string> Skip(Session session, string user, DateTimeOffset now, TimeSpan breakDuration) =>
        SessionRules.CheckNotDeleted(session).Bind(DrivingRules.CheckNoBreak).Bind<ImmutableArray<object>>(session => session switch
        {
            { Timer: TimerPhase.DriveRunning } =>
                Result.Ok<ImmutableArray<object>>(
                    [new DriveSkipped(session.Id, user, now), DrivingRules.StartBreak(session, user, now, breakDuration)]),
            _ => Result.Error("No Drive runs."),
        });
}
