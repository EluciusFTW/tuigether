using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Sessions;

public static class CreateSessionEndpoint
{
    public static ProblemDetails Validate(CreateSession command) =>
        Problems.FromRule(SessionRules.CheckCreate(command));

    [WolverinePost("/api/sessions")]
    public static (Created<CreateSessionResponse>, IStartStream) Post(
        CreateSession command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        TimeProvider clock)
    {
        var sessionId = Guid.CreateVersion7();
        var created = new SessionCreated(
            sessionId,
            command.Title,
            command.Repository,
            command.Branch,
            userName,
            clock.GetUtcNow());

        return (
            TypedResults.Created(default(string), new CreateSessionResponse(sessionId)),
            MartenOps.StartStream<Session>(sessionId, created));
    }
}
