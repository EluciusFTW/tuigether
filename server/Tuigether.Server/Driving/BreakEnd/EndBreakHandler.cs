using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Push;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.BreakEnd;

public static class EndBreakHandler
{
    // A scheduled end can outlive its Session, for example after a delete.
    public static (Events, BroadcastSessionChanged?) Handle(
        EndBreak end,
        [WriteAggregate(Required = false)] Session? session)
    {
        if (session is null || session.IsDeleted)
        {
            return ([], null);
        }

        if (session.Timer != TimerPhase.Break || session.BreakEndsAt != end.EndsAt)
        {
            return ([], null);
        }

        return ([new BreakEnded(session.Id, end.EndsAt)], new BroadcastSessionChanged(end.SessionId));
    }
}
