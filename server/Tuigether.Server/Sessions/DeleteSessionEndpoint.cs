using JasperFx.Events;
using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Push;
using Wolverine.Http;
using Wolverine.Marten;
using Wolverine.Persistence;

namespace Tuigether.Server.Sessions;

public static class DeleteSessionEndpoint
{
    public static ProblemDetails Validate(Session session) =>
        Problems.FromRule(SessionRules.CheckNotDeleted(session));

    // The Archived event makes Marten archive the stream in the same transaction.
    // The events stay in the database for statistics.
    [WolverinePost("/api/sessions/delete")]
    [EmptyResponse]
    public static (Events, BroadcastSessionChanged) Post(
        DeleteSession command,
        [WriteAggregate(OnMissing = OnMissing.ProblemDetailsWith404)] Session session,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        TimeProvider clock) =>
        (
            [new SessionDeleted(session.Id, userName, clock.GetUtcNow()), new Archived("Session deleted")],
            new BroadcastSessionChanged(session.Id));
}
