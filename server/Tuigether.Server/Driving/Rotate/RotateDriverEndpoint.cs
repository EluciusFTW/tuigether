using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.Rotate;

/// <summary>
/// Invokes <see cref="RotateDriverHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class RotateDriverEndpoint
{
    [WolverinePost(CommandRoutes.RotateDriver)]
    public static Task<IResult> Post(
        RotateDriver command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new RotateDriverBy(command, userName), cancellation);
}

/// <param name="User">The user name of the request.</param>
public sealed record RotateDriverBy(RotateDriver Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class RotateDriverHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        RotateDriverBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var (outcome, events, broadcast) = SessionCommand.Decide(session, current => RotateDriverRules.Rotate(current, request.Command, request.User, now));
        return (outcome, events, broadcast, DrivingRules.ScheduleEnds(events, now));
    }
}
