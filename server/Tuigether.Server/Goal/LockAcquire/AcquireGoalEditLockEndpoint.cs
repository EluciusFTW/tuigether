using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Goal.LockAcquire;

/// <summary>
/// Invokes <see cref="AcquireGoalEditLockHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class AcquireGoalEditLockEndpoint
{
    [WolverinePost(CommandRoutes.AcquireGoalEditLock)]
    public static Task<IResult> Post(
        AcquireGoalEditLock command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new AcquireGoalEditLockBy(command, userName), cancellation);
}

/// <inheritdoc cref="Sessions.Create.CreateSessionBy"/>
public sealed record AcquireGoalEditLockBy(AcquireGoalEditLock Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class AcquireGoalEditLockHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        AcquireGoalEditLockBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => AcquireGoalEditLockRules.Acquire(current, request.User, clock.GetUtcNow()));
}
