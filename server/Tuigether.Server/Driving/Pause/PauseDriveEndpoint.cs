using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.Pause;

/// <summary>
/// Invokes <see cref="PauseDriveHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class PauseDriveEndpoint
{
    [WolverinePost(CommandRoutes.PauseDrive)]
    public static Task<IResult> Post(PauseDrive command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class PauseDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        PauseDrive command,
        [WriteAggregate(Required = false)] Session? session,
        Envelope envelope,
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

        if (session is not { Timer: TimerPhase.DriveRunning, DriveEndsAt: { } endsAt })
        {
            return SessionCommand.Reject("No Drive runs.");
        }

        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new DrivePaused(session.Id, SessionRules.TimeLeft(endsAt, now), SessionCommand.UserOf(envelope), now)]);
    }
}
