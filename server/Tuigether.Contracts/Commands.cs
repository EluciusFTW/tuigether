namespace Tuigether.Contracts;

/// <summary>
/// The HTTP routes of the Commands. Each Command is a POST with the Command as its JSON body.
/// </summary>
public static class CommandRoutes
{
    public const string CreateSession = "/api/sessions";

    public const string DeleteSession = "/api/sessions/delete";
}

public sealed record CreateSession(string Title, string Repository, string Branch);

public sealed record CreateSessionResponse(Guid SessionId);

public sealed record DeleteSession(Guid SessionId);
