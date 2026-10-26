using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Add;

/// <summary>
/// Invokes <see cref="AddTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class AddTodoEndpoint
{
    [WolverinePost(CommandRoutes.AddTodo)]
    public static Task<IResult> Post(
        AddTodo command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new AddTodoBy(command, userName), cancellation);
}

/// <inheritdoc cref="Sessions.Create.CreateSessionBy"/>
public sealed record AddTodoBy(AddTodo Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class AddTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        AddTodoBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => AddTodoRules.Add(current, request.Command, request.User, clock.GetUtcNow()));
}
