using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Driving.Resume;

public static class ResumeDriveRules
{
    public static Result<ImmutableArray<object>, string> Resume(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            { Timer: TimerPhase.DrivePaused } =>
                Result.Ok<ImmutableArray<object>>([new DriveResumed(session.Id, now + session.DriveRemaining, user, now)]),
            _ => Result.Error("No Drive is paused."),
        });
}
