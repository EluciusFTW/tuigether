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
    public static Task<IResult> Post(RotateDriver command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class RotateDriverHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        RotateDriver command,
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

        if (command.CurrentDriver != session.Driver)
        {
            return SessionCommand.Reject($"Another Participant changed the Driver to {session.Driver ?? "nobody"}.");
        }

        if (!session.Participants.Contains(command.NextDriver))
        {
            return SessionCommand.Reject($"{command.NextDriver} is not a Participant of the Session.");
        }

        if (command.Duration <= TimeSpan.Zero)
        {
            return SessionCommand.Reject("A Drive needs a duration longer than zero.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        Events events = [];

        switch (session.Timer)
        {
            case TimerPhase.DriveRunning or TimerPhase.DrivePaused:
                events.Add(new DriveSwitched(session.Id, user, now));
                break;
            case TimerPhase.Break:
                events.Add(new BreakSkipped(session.Id, user, now));
                break;
        }

        events.Add(new DriverChanged(session.Id, command.NextDriver, user, now));
        events.Add(new DriveStarted(session.Id, command.NextDriver, now + command.Duration, user, now));

        return SessionCommand.Accept(session, events, DrivingRules.ScheduleEnds(events, now));
    }
}
