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
    public static Task<IResult> Post(SkipDrive command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class SkipDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        SkipDrive command,
        [WriteAggregate(Required = false)] Session? session,
        Envelope envelope,
        IOptions<ServerOptions> options,
        TimeProvider clock)
    {
        if (session is null)
        {
            return SessionCommand.NotFound();
        }

        if (session.IsDeleted)
        {
            return SessionCommand.Reject(SessionRules.DeletedReason);
        }

        if (session.Timer == TimerPhase.Break)
        {
            return SessionCommand.Reject(DrivingRules.BreakRunsReason);
        }

        if (session.Timer != TimerPhase.DriveRunning)
        {
            return SessionCommand.Reject("No Drive runs.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        Events events = [new DriveSkipped(session.Id, user, now), DrivingRules.StartBreak(session, user, now, options.Value.BreakDuration)];
        return SessionCommand.Accept(session, events, DrivingRules.ScheduleEnds(events, now));
    }
}
