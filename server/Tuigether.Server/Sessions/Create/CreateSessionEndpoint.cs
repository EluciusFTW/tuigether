using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Sessions.Create;

/// <summary>
/// Invokes <see cref="CreateSessionHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class CreateSessionEndpoint
{
    [WolverinePost(CommandRoutes.CreateSession)]
    public static Task<IResult> Post(
        CreateSession command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new CreateSessionBy(command, userName), cancellation);
}

/// <param name="User">The user name of the request.</param>
public sealed record CreateSessionBy(CreateSession Command, string User);

public static class CreateSessionHandler
{
    public static (CommandOutcome, IMartenOp, BroadcastSessionChanged?) Handle(CreateSessionBy request, TimeProvider clock) =>
        CreateSessionRules.Create(request.Command, Guid.CreateVersion7(), request.User, clock.GetUtcNow())
            .Match<(CommandOutcome, IMartenOp, BroadcastSessionChanged?)>(
                static created => (
                    new CommandOutcome.Created(created.SessionId),
                    MartenOps.StartStream<Session>(created.SessionId, created),
                    new BroadcastSessionChanged(created.SessionId)),
                static reason => (new CommandOutcome.Rejected(reason), MartenOps.Nothing(), null));
}
