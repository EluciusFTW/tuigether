using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.Complete;

/// <summary>
/// Invokes <see cref="CompleteTodoHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class CompleteTodoEndpoint
{
    [WolverinePost(CommandRoutes.CompleteTodo)]
    public static Task<IResult> Post(CompleteTodo command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class CompleteTodoHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        CompleteTodo command,
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

        if (todo.Completed)
        {
            return SessionCommand.Reject("The todo is completed already.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new TodoCompleted(session.Id, todo.Id, user, now)]);
    }
}
