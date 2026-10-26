using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Reopen;

/// <summary>
/// Invokes <see cref="ReopenTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ReopenTodoEndpoint
{
    [WolverinePost(CommandRoutes.ReopenTodo)]
    public static Task<IResult> Post(ReopenTodo command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class ReopenTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        ReopenTodo command,
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

        if (!todo.Completed)
        {
            return SessionCommand.Reject("The todo is open already.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new TodoReopened(session.Id, todo.Id, user, now)]);
    }
}
