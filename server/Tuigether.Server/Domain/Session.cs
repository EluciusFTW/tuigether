using System.Collections.Immutable;
using Tuigether.Contracts;

namespace Tuigether.Server.Domain;

/// <summary>
/// The write model of one Session stream. The Command handlers and the presence handlers decide against it.
/// </summary>
public sealed record Session(Guid Id, bool IsDeleted)
{
    public SessionStatus Status { get; init; } = SessionStatus.Created;

    /// <summary>
    /// The user names of the Participants, in the order of their join.
    /// </summary>
    public ImmutableArray<string> Participants { get; init; } = [];

    public string? Driver { get; init; }

    public TimerPhase Timer { get; init; } = TimerPhase.Idle;

    /// <summary>
    /// When the running Drive ends. Null when no Drive runs.
    /// </summary>
    public DateTimeOffset? DriveEndsAt { get; init; }

    /// <summary>
    /// The time that the paused Drive has left.
    /// </summary>
    public TimeSpan DriveRemaining { get; init; }

    public static Session Create(SessionCreated created) => new(created.SessionId, IsDeleted: false);

    public static Session Apply(SessionDeleted _, Session session) => session with { IsDeleted = true };

    public static Session Apply(SessionStarted _, Session session) => session with { Status = SessionStatus.Started };

    public static Session Apply(SessionFinished _, Session session) => session with { Status = SessionStatus.Finished };

    public static Session Apply(ParticipantJoined joined, Session session) =>
        session with { Participants = [.. session.Participants, joined.User] };

    public static Session Apply(ParticipantLeft left, Session session) =>
        session with { Participants = session.Participants.Remove(left.User) };

    public static Session Apply(DriverChanged changed, Session session) => session with { Driver = changed.Driver };

    public static Session Apply(DriveStarted started, Session session) =>
        session with { Timer = TimerPhase.DriveRunning, DriveEndsAt = started.EndsAt };

    public static Session Apply(DrivePaused paused, Session session) =>
        session with { Timer = TimerPhase.DrivePaused, DriveEndsAt = null, DriveRemaining = paused.Remaining };

    public static Session Apply(DriveResumed resumed, Session session) =>
        session with { Timer = TimerPhase.DriveRunning, DriveEndsAt = resumed.EndsAt };
}
