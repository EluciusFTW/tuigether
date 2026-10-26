using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.BreakSkip;

/// <summary>
/// Invokes <see cref="SkipBreakHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class SkipBreakEndpoint
{
    [WolverinePost(CommandRoutes.SkipBreak)]
    public static Task<IResult> Post(
        SkipBreak command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new SkipBreakBy(command, userName), cancellation);
}

/// <inheritdoc cref="Sessions.Create.CreateSessionBy"/>
public sealed record SkipBreakBy(SkipBreak Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class SkipBreakHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        SkipBreakBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => SkipBreakRules.Skip(current, request.User, clock.GetUtcNow()));
}
