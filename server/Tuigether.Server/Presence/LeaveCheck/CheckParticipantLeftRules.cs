using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Presence.LeaveCheck;

public static class CheckParticipantLeftRules
{
    /// <summary>
    /// The events when the grace period after a closed Client of <paramref name="user"/> ends.
    /// </summary>
    /// <param name="hasOpenClient">Another Client of the User has the Session open.</param>
    public static ImmutableArray<object> Leave(Session session, string user, bool hasOpenClient, DateTimeOffset now) =>
        session switch
        {
            { IsDeleted: true } => [],
            _ when hasOpenClient || !session.Participants.Contains(user) => [],
            _ =>
                [
                    new ParticipantLeft(session.Id, user, now),
                    .. LeaveDrive(session, user, now),
                    .. LeaveNotes(session, user, now),
                    .. Finish(session, user, now),
                ],
        };

    /// <summary>
    /// A Drive must not run with nobody at the keyboard, so the leave of the Driver pauses it and clears the Driver.
    /// </summary>
    private static ImmutableArray<object> LeaveDrive(Session session, string user, DateTimeOffset now) =>
        session switch
        {
            _ when session.Driver != user => [],
            { Timer: TimerPhase.DriveRunning, DriveEndsAt: { } endsAt } =>
                [new DrivePaused(session.Id, SessionRules.TimeLeft(endsAt, now), user, now), new DriverChanged(session.Id, null, user, now)],
            _ => [new DriverChanged(session.Id, null, user, now)],
        };

    /// <summary>
    /// The Edit lock has no time limit, so the leave of its holder is the only release without an end of the edit.
    /// </summary>
    private static ImmutableArray<object> LeaveNotes(Session session, string user, DateTimeOffset now) =>
        session.NotesEditLockHolder == user ? [new NotesEditingEnded(session.Id, user, now)] : [];

    private static ImmutableArray<object> Finish(Session session, string user, DateTimeOffset now) =>
        session is { Status: SessionStatus.Started, Participants.Length: 1 } ? [new SessionFinished(session.Id, user, now)] : [];
}
