using Microsoft.Extensions.Options;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.Finish;

public static class FinishDriveHandler
{
    // A scheduled end can outlive its Session, for example after a delete.
    public static (Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        FinishDrive finish,
        [WriteAggregate(Required = false)] Session? session,
        IOptions<ServerOptions> options,
        TimeProvider clock)
    {
        if (session is null)
        {
            return ([], null, []);
        }

        var driveRuns = session is { IsDeleted: false, Timer: TimerPhase.DriveRunning };
        var endIsUnchanged = session.DriveEndsAt == finish.EndsAt;
        if (!driveRuns || !endIsUnchanged)
        {
            return ([], null, []);
        }

        // The Break starts at the end of the Drive, so all Clients count it from the same time.
        Events events =
        [
            new DriveFinished(session.Id, finish.EndsAt),
            DrivingRules.StartBreak(session, user: null, finish.EndsAt, options.Value.BreakDuration),
        ];

        return (events, new BroadcastSessionChanged(finish.SessionId), DrivingRules.ScheduleEnds(events, clock.GetUtcNow()));
    }
}
