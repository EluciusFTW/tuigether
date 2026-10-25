using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Todos.Move;

public static class MoveTodoRules
{
    public static Result<ImmutableArray<object>, string> Move(Session session, MoveTodo command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Bind(session => TodoRules.CheckExists(session, command.TodoId))
            .Bind<ImmutableArray<object>>(todo => command.NewPosition >= 0 && command.NewPosition < session.Todos.Length
                ? Result.Ok<ImmutableArray<object>>([new TodoMoved(session.Id, todo.Id, command.NewPosition, user, now)])
                : Result.Error($"The position {command.NewPosition} is not in the range 0 to {session.Todos.Length - 1}."));
}
