namespace Tuigether.Contracts;

public sealed record SessionCreated(
    Guid SessionId,
    string Title,
    string Repository,
    string Branch,
    string User,
    DateTimeOffset At);

public sealed record SessionDeleted(Guid SessionId, string User, DateTimeOffset At);

/// <param name="User">The Participant whose join started the Session.</param>
public sealed record SessionStarted(Guid SessionId, string User, DateTimeOffset At);

/// <param name="User">The last Participant who left.</param>
public sealed record SessionFinished(Guid SessionId, string User, DateTimeOffset At);

public sealed record ParticipantJoined(Guid SessionId, string User, string Avatar, DateTimeOffset At);

public sealed record ParticipantLeft(Guid SessionId, string User, DateTimeOffset At);

/// <param name="Driver">The new Driver, or null when the Session has no Driver.</param>
public sealed record DriverChanged(Guid SessionId, string? Driver, string User, DateTimeOffset At);

/// <param name="EndsAt">The time on the server clock when the Drive ends.</param>
public sealed record DriveStarted(Guid SessionId, string Driver, DateTimeOffset EndsAt, string User, DateTimeOffset At);

/// <param name="Remaining">The time that the Drive has left.</param>
public sealed record DrivePaused(Guid SessionId, TimeSpan Remaining, string User, DateTimeOffset At);

/// <param name="EndsAt">The time on the server clock when the Drive ends.</param>
public sealed record DriveResumed(Guid SessionId, DateTimeOffset EndsAt, string User, DateTimeOffset At);

/// <summary>
/// A rotation ended the running or paused Drive before its end.
/// </summary>
public sealed record DriveSwitched(Guid SessionId, string User, DateTimeOffset At);

/// <summary>
/// The running Drive reached its end. No Participant causes it.
/// </summary>
/// <param name="At">The end of the Drive. The server appends the event up to one polling interval later.</param>
public sealed record DriveFinished(Guid SessionId, DateTimeOffset At);

/// <summary>
/// A Participant ended the running Drive before its end.
/// </summary>
public sealed record DriveSkipped(Guid SessionId, string User, DateTimeOffset At);

/// <summary>
/// The Break for all Participants. It follows a finished or skipped Drive.
/// </summary>
/// <param name="EndsAt">The time on the server clock when the Break ends.</param>
/// <param name="User">The Participant who skipped the Drive. Null when the Drive reached its end.</param>
public sealed record BreakStarted(Guid SessionId, DateTimeOffset EndsAt, string? User, DateTimeOffset At);

/// <summary>
/// A Participant ended the Break before its end, also by a rotation.
/// </summary>
public sealed record BreakSkipped(Guid SessionId, string User, DateTimeOffset At);

/// <summary>
/// The Break reached its end. No Participant causes it.
/// </summary>
/// <param name="At">The end of the Break. The server appends the event up to one polling interval later.</param>
public sealed record BreakEnded(Guid SessionId, DateTimeOffset At);
