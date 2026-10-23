using Tuigether.Contracts;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Sessions.Create;

public static class CreateSessionRules
{
    public static Result<CreateSession, string> CheckCreate(CreateSession command) =>
        string.IsNullOrWhiteSpace(command.Title)
            ? Result.Error("A Session needs a title.")
            : Result.Ok(command);

    public static Result<SessionCreated, string> Create(CreateSession command, Guid sessionId, string user, DateTimeOffset now) =>
        CheckCreate(command).Map(command =>
            new SessionCreated(sessionId, command.Title, command.Repository, command.Branch, user, now));
}
