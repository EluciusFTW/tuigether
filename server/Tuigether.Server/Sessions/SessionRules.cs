using Tuigether.Contracts;

namespace Tuigether.Server.Sessions;

public static class SessionRules
{
    public static Result<CreateSession, string> CheckCreate(CreateSession command) =>
        string.IsNullOrWhiteSpace(command.Title)
            ? Result.Error("A Session needs a title.")
            : Result.Ok(command);

    /// <summary>
    /// Every Command on an existing Session passes this rule first.
    /// </summary>
    public static Result<Session, string> CheckNotDeleted(Session session) =>
        session.IsDeleted
            ? Result.Error("The Session is deleted.")
            : Result.Ok(session);
}
