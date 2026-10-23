using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Sessions;

namespace Tuigether.Server.Driving;

public static class DrivingRules
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
            _ => Result.Ok(StartDrive(session, command, user, now)),
        });

    public static Result<ImmutableArray<object>, string> Pause(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            { Timer: TimerPhase.DriveRunning, DriveEndsAt: { } endsAt } =>
                Result.Ok<ImmutableArray<object>>([new DrivePaused(session.Id, Remaining(endsAt, now), user, now)]),
            _ => Result.Error("No Drive runs."),
        });

    public static Result<ImmutableArray<object>, string> Resume(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            { Timer: TimerPhase.DrivePaused } =>
                Result.Ok<ImmutableArray<object>>([new DriveResumed(session.Id, now + session.DriveRemaining, user, now)]),
            _ => Result.Error("No Drive is paused."),
        });

    /// <summary>
    /// The events when <paramref name="user"/> leaves the Session. A Drive must not run with nobody at the keyboard, so
    /// the leave of the Driver pauses it and clears the Driver.
    /// </summary>
    public static ImmutableArray<object> Leave(Session session, string user, DateTimeOffset now) =>
        session switch
        {
            _ when session.Driver != user => [],
            { Timer: TimerPhase.DriveRunning, DriveEndsAt: { } endsAt } =>
                [new DrivePaused(session.Id, Remaining(endsAt, now), user, now), new DriverChanged(session.Id, null, user, now)],
            _ => [new DriverChanged(session.Id, null, user, now)],
        };

    private static ImmutableArray<object> StartDrive(Session session, RotateDriver command, string user, DateTimeOffset now) =>
        [
            new DriverChanged(session.Id, command.NextDriver, user, now),
            new DriveStarted(session.Id, command.NextDriver, now + command.Duration, user, now),
        ];

    private static TimeSpan Remaining(DateTimeOffset endsAt, DateTimeOffset now) =>
        endsAt > now ? endsAt - now : TimeSpan.Zero;
}
