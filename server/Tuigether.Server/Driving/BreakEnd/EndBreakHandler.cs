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
        var events = session is null ? [] : EndBreakRules.End(session, end.EndsAt);
        return ([.. events], events.IsEmpty ? null : new BroadcastSessionChanged(end.SessionId));
    }
}
