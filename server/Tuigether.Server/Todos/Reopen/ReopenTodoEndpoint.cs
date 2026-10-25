using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Reopen;

/// <summary>
/// Invokes <see cref="ReopenTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ReopenTodoEndpoint
{
    [WolverinePost(CommandRoutes.ReopenTodo)]
    public static Task<IResult> Post(
        ReopenTodo command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new ReopenTodoBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record ReopenTodoBy(ReopenTodo Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class ReopenTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        ReopenTodoBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => ReopenTodoRules.Reopen(current, request.Command, request.User, clock.GetUtcNow()));
}
