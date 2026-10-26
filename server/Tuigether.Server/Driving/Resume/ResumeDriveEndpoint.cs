using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.Resume;

/// <summary>
/// Invokes <see cref="ResumeDriveHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ResumeDriveEndpoint
{
    [WolverinePost(CommandRoutes.ResumeDrive)]
    public static Task<IResult> Post(ResumeDrive command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class ResumeDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        ResumeDrive command,
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

        if (session.Timer != TimerPhase.DrivePaused)
        {
            return SessionCommand.Reject("No Drive is paused.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        Events events = [new DriveResumed(session.Id, now + session.DriveRemaining, user, now)];
        return SessionCommand.Accept(session, events, DrivingRules.ScheduleEnds(events, now));
    }
}
