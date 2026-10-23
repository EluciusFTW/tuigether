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
