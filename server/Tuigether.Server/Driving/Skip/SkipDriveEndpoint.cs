using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.Skip;

/// <summary>
/// Invokes <see cref="SkipDriveHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class SkipDriveEndpoint
{
    [WolverinePost(CommandRoutes.SkipDrive)]
    public static Task<IResult> Post(
        SkipDrive command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new SkipDriveBy(command, userName), cancellation);
}

/// <inheritdoc cref="Sessions.Create.CreateSessionBy"/>
public sealed record SkipDriveBy(SkipDrive Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class SkipDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        SkipDriveBy request,
        [WriteAggregate(Required = false)] Session? session,
        IOptions<ServerOptions> options,
        TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var (outcome, events, broadcast) =
            SessionCommand.Decide(session, current => SkipDriveRules.Skip(current, request.User, now, options.Value.BreakDuration));
        return (outcome, events, broadcast, DrivingRules.ScheduleEnds(events, now));
    }
}
