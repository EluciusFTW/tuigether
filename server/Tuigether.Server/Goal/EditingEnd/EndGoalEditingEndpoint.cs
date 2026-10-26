using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Goal.EditingEnd;

/// <summary>
/// Invokes <see cref="EndGoalEditingHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class EndGoalEditingEndpoint
{
    [WolverinePost(CommandRoutes.EndGoalEditing)]
    public static Task<IResult> Post(EndGoalEditing command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class EndGoalEditingHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        EndGoalEditing command,
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
        if (session.GoalEditLockHolder != user)
        {
            return SessionCommand.Reject($"{user} does not hold the Edit lock of the Goal.");
        }

        var now = clock.GetUtcNow();
        return session.GoalText == command.Text
            ? SessionCommand.Accept(session, [new GoalEditingEnded(session.Id, user, now)])
            : SessionCommand.Accept(session, [new GoalChanged(session.Id, command.Text, user, now), new GoalEditingEnded(session.Id, user, now)]);
    }
}
