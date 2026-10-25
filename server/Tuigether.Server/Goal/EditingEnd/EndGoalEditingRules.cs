using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Goal.EditingEnd;

public static class EndGoalEditingRules
{
    public static Result<ImmutableArray<object>, string> End(Session session, string text, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            _ when session.GoalEditLockHolder != user => Result.Error($"{user} does not hold the Edit lock of the Goal."),
            _ when session.GoalText == text => Result.Ok<ImmutableArray<object>>([new GoalEditingEnded(session.Id, user, now)]),
            _ => Result.Ok<ImmutableArray<object>>(
                [new GoalChanged(session.Id, text, user, now), new GoalEditingEnded(session.Id, user, now)]),
        });
}
