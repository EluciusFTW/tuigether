using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Move;

/// <summary>
/// Invokes <see cref="MoveTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class MoveTodoEndpoint
{
    [WolverinePost(CommandRoutes.MoveTodo)]
    public static Task<IResult> Post(
        MoveTodo command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new MoveTodoBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record MoveTodoBy(MoveTodo Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class MoveTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        MoveTodoBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => MoveTodoRules.Move(current, request.Command, request.User, clock.GetUtcNow()));
}
