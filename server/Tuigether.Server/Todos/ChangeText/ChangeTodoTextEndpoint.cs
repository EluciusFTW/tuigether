using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Todos.ChangeText;

/// <summary>
/// Invokes <see cref="ChangeTodoTextHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ChangeTodoTextEndpoint
{
    [WolverinePost(CommandRoutes.ChangeTodoText)]
    public static Task<IResult> Post(ChangeTodoText command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class ChangeTodoTextHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        ChangeTodoText command,
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

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new TodoTextChanged(session.Id, todo.Id, command.Text, user, now)]);
    }
}
