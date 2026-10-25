using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.ChangeText;

/// <summary>
/// Invokes <see cref="ChangeTodoTextHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ChangeTodoTextEndpoint
{
    [WolverinePost(CommandRoutes.ChangeTodoText)]
    public static Task<IResult> Post(
        ChangeTodoText command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new ChangeTodoTextBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record ChangeTodoTextBy(ChangeTodoText Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class ChangeTodoTextHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        ChangeTodoTextBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => ChangeTodoTextRules.Change(current, request.Command, request.User, clock.GetUtcNow()));
}
