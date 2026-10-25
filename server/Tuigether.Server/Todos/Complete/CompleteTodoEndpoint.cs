using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Complete;

/// <summary>
/// Invokes <see cref="CompleteTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class CompleteTodoEndpoint
{
    [WolverinePost(CommandRoutes.CompleteTodo)]
    public static Task<IResult> Post(
        CompleteTodo command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new CompleteTodoBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record CompleteTodoBy(CompleteTodo Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class CompleteTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        CompleteTodoBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => CompleteTodoRules.Complete(current, request.Command, request.User, clock.GetUtcNow()));
}
