using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Driving.BreakEnd;

public static class EndBreakRules
{
    /// <summary>
    /// The events at <paramref name="endsAt"/>, the end that an <see cref="EndBreak"/> was scheduled for.
    /// </summary>
    public static ImmutableArray<object> End(Session session, DateTimeOffset endsAt) =>
        session switch
        {
            { IsDeleted: true } => [],
            { Timer: TimerPhase.Break } when session.BreakEndsAt == endsAt => [new BreakEnded(session.Id, endsAt)],
            _ => [],
        };
}
