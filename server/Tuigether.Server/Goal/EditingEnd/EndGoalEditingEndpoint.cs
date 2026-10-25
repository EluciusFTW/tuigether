using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Goal.EditingEnd;

/// <summary>
/// Invokes <see cref="EndGoalEditingHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class EndGoalEditingEndpoint
{
    [WolverinePost(CommandRoutes.EndGoalEditing)]
    public static Task<IResult> Post(
        EndGoalEditing command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new EndGoalEditingBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record EndGoalEditingBy(EndGoalEditing Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class EndGoalEditingHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        EndGoalEditingBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => EndGoalEditingRules.End(current, request.Command.Text, request.User, clock.GetUtcNow()));
}
