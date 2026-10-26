using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Wolverine;

namespace Tuigether.Server.Driving;

public static class DrivingRules
{
    /// <summary>
    /// During a Break no Drive exists to pause, resume or skip. Only a rotation starts the next Drive.
    /// </summary>
    public const string BreakRunsReason = "A Break runs.";

    /// <summary>
    /// The Break after a finished or skipped Drive, from <paramref name="at"/> on.
    /// </summary>
    public static BreakStarted StartBreak(Session session, string? user, DateTimeOffset at, TimeSpan breakDuration) =>
        new(session.Id, at + breakDuration, user, at);

    /// <summary>
    /// One scheduled <see cref="FinishDrive"/> or <see cref="EndBreak"/> for each Drive and Break that
    /// <paramref name="events"/> start.
    /// </summary>
    /// <remarks>
    /// Wolverine schedules on the system clock, and the events hold times of the <see cref="TimeProvider"/>. So the
    /// message runs after the delay from <paramref name="now"/>, not at the end itself.
    /// </remarks>
    public static OutgoingMessages ScheduleEnds(IEnumerable<object> events, DateTimeOffset now)
    {
        var messages = new OutgoingMessages();
        foreach (var appended in events)
        {
            switch (appended)
            {
                case DriveStarted started:
                    messages.Delay(new FinishDrive(started.SessionId, started.EndsAt), SessionRules.TimeLeft(started.EndsAt, now));
                    break;
                case DriveResumed resumed:
                    messages.Delay(new FinishDrive(resumed.SessionId, resumed.EndsAt), SessionRules.TimeLeft(resumed.EndsAt, now));
                    break;
                case BreakStarted started:
                    messages.Delay(new EndBreak(started.SessionId, started.EndsAt), SessionRules.TimeLeft(started.EndsAt, now));
                    break;
            }
        }

        return messages;
    }
}
