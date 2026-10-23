using System.Collections.Immutable;
using JasperFx.Events;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Sessions.Delete;

public static class DeleteSessionRules
{
    /// <summary>
    /// The Archived event makes Marten archive the stream in the same transaction. The events stay for statistics.
    /// </summary>
    public static Result<ImmutableArray<object>, string> Delete(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Map<ImmutableArray<object>>(session =>
            [new SessionDeleted(session.Id, user, now), new Archived("Session deleted")]);
}
