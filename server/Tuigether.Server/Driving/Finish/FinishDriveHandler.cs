using Microsoft.Extensions.Options;
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
        var events = session is null ? [] : FinishDriveRules.Finish(session, finish.EndsAt, options.Value.BreakDuration);
        return (
            [.. events],
            events.IsEmpty ? null : new BroadcastSessionChanged(finish.SessionId),
            DrivingRules.ScheduleEnds(events, clock.GetUtcNow()));
    }
}
