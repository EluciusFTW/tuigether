using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Driving.Rotate;

public static class RotateDriverRules
{
    public static Result<ImmutableArray<object>, string> Rotate(Session session, RotateDriver command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            _ when command.CurrentDriver != session.Driver =>
                Result.Error($"Another Participant changed the Driver to {session.Driver ?? "nobody"}."),
            _ when !session.Participants.Contains(command.NextDriver) =>
                Result.Error($"{command.NextDriver} is not a Participant of the Session."),
            _ when command.Duration <= TimeSpan.Zero => Result.Error("A Drive needs a duration longer than zero."),
            { Timer: TimerPhase.DriveRunning or TimerPhase.DrivePaused } =>
                Result.Ok<ImmutableArray<object>>([new DriveSwitched(session.Id, user, now), .. StartDrive(session, command, user, now)]),
            { Timer: TimerPhase.Break } =>
                Result.Ok<ImmutableArray<object>>([new BreakSkipped(session.Id, user, now), .. StartDrive(session, command, user, now)]),
            _ => Result.Ok(StartDrive(session, command, user, now)),
        });

    private static ImmutableArray<object> StartDrive(Session session, RotateDriver command, string user, DateTimeOffset now) =>
        [
            new DriverChanged(session.Id, command.NextDriver, user, now),
            new DriveStarted(session.Id, command.NextDriver, now + command.Duration, user, now),
        ];
}
