using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Goal.LockAcquire;

public static class AcquireGoalEditLockRules
{
    /// <summary>
    /// The holder of the Edit lock keeps it, so an acquire by the holder appends nothing.
    /// </summary>
    public static Result<ImmutableArray<object>, string> Acquire(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            // A holder who is no Participant never leaves, so no leave check would release the Edit lock.
            _ when !session.Participants.Contains(user) => Result.Error($"{user} is not a Participant of the Session."),
            { GoalEditLockHolder: null } => Result.Ok<ImmutableArray<object>>([new GoalEditingStarted(session.Id, user, now)]),
            { GoalEditLockHolder: var holder } when holder == user => Result.Ok(ImmutableArray<object>.Empty),
            { GoalEditLockHolder: var holder } => Result.Error($"{holder} edits the Goal."),
        });
}
