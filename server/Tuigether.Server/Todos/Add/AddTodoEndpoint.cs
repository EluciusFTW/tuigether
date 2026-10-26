using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Add;

/// <summary>
/// Invokes <see cref="AddTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class AddTodoEndpoint
{
    [WolverinePost(CommandRoutes.AddTodo)]
    public static Task<IResult> Post(AddTodo command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class AddTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        AddTodo command,
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

        if (session.Todos.Any(todo => todo.Id == command.TodoId))
        {
            return SessionCommand.Reject("A todo with this id exists.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new TodoAdded(session.Id, command.TodoId, command.Text, user, now)]);
    }
}
