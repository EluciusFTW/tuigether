using System.Collections.Immutable;
using JasperFx.Events;
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

    public static Result<SessionCreated, string> Create(CreateSession command, Guid sessionId, string user, DateTimeOffset now) =>
        CheckCreate(command).Map(command =>
            new SessionCreated(sessionId, command.Title, command.Repository, command.Branch, user, now));

    /// <summary>
    /// The Archived event makes Marten archive the stream in the same transaction. The events stay for statistics.
    /// </summary>
    public static Result<ImmutableArray<object>, string> Delete(Session session, string user, DateTimeOffset now) =>
        CheckNotDeleted(session).Map<ImmutableArray<object>>(session =>
            [new SessionDeleted(session.Id, user, now), new Archived("Session deleted")]);
}
