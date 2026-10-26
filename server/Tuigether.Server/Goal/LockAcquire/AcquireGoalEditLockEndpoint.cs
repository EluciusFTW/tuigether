using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Goal.LockAcquire;

/// <summary>
/// Invokes <see cref="AcquireGoalEditLockHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class AcquireGoalEditLockEndpoint
{
    [WolverinePost(CommandRoutes.AcquireGoalEditLock)]
    public static Task<IResult> Post(AcquireGoalEditLock command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class AcquireGoalEditLockHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        AcquireGoalEditLock command,
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

        var user = SessionCommand.UserOf(envelope);

        // A holder who is no Participant never leaves, so no leave check would release the Edit lock.
        if (!session.Participants.Contains(user))
        {
            return SessionCommand.Reject($"{user} is not a Participant of the Session.");
        }

        // The holder of the Edit lock keeps it, so an acquire by the holder appends nothing.
        if (session.GoalEditLockHolder == user)
        {
            return SessionCommand.Accept(session, []);
        }

        if (session.GoalEditLockHolder is { } holder)
        {
            return SessionCommand.Reject($"{holder} edits the Goal.");
        }

        return SessionCommand.Accept(session, [new GoalEditingStarted(session.Id, user, clock.GetUtcNow())]);
    }
}
