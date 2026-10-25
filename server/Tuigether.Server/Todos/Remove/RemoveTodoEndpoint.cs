using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Remove;

/// <summary>
/// Invokes <see cref="RemoveTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class RemoveTodoEndpoint
{
    [WolverinePost(CommandRoutes.RemoveTodo)]
    public static Task<IResult> Post(
        RemoveTodo command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new RemoveTodoBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record RemoveTodoBy(RemoveTodo Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class RemoveTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        RemoveTodoBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => RemoveTodoRules.Remove(current, request.Command, request.User, clock.GetUtcNow()));
}
