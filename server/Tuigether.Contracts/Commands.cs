namespace Tuigether.Contracts;

public sealed record CreateSession(string Title, string Repository, string Branch);

public sealed record CreateSessionResponse(Guid SessionId);

public sealed record DeleteSession(Guid SessionId);
