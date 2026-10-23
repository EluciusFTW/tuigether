using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Driving.Pause;

public static class PauseDriveRules
{
    public static Result<ImmutableArray<object>, string> Pause(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            { Timer: TimerPhase.DriveRunning, DriveEndsAt: { } endsAt } =>
                Result.Ok<ImmutableArray<object>>([new DrivePaused(session.Id, SessionRules.DriveRemaining(endsAt, now), user, now)]),
            _ => Result.Error("No Drive runs."),
        });
}
