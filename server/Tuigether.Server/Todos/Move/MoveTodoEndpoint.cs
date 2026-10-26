using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Move;

/// <summary>
/// Invokes <see cref="MoveTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class MoveTodoEndpoint
{
    [WolverinePost(CommandRoutes.MoveTodo)]
    public static Task<IResult> Post(MoveTodo command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class MoveTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        MoveTodo command,
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

        if (session.Todos.FirstOrDefault(todo => todo.Id == command.TodoId) is not { } todo)
        {
            return SessionCommand.Reject("The todo does not exist.");
        }

        var positionIsInRange = command.NewPosition >= 0 && command.NewPosition < session.Todos.Length;
        if (!positionIsInRange)
        {
            return SessionCommand.Reject($"The position {command.NewPosition} is not in the range 0 to {session.Todos.Length - 1}.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new TodoMoved(session.Id, todo.Id, command.NewPosition, user, now)]);
    }
}
