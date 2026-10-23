using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Sessions;

/// <summary>
/// Invokes <see cref="DeleteSessionHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class DeleteSessionEndpoint
{
    [WolverinePost(CommandRoutes.DeleteSession)]
    public static Task<IResult> Post(
        DeleteSession command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new DeleteSessionBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.RotateDriverBy"/>
public sealed record DeleteSessionBy(DeleteSession Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class DeleteSessionHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        DeleteSessionBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => SessionRules.Delete(current, request.User, clock.GetUtcNow()));
}
