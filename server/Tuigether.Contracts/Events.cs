namespace Tuigether.Contracts;

public sealed record SessionCreated(
    Guid SessionId,
    string Title,
    string Repository,
    string Branch,
    string User,
    DateTimeOffset At);

public sealed record SessionDeleted(Guid SessionId, string User, DateTimeOffset At);
