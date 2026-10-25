using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Todos.Remove;

public static class RemoveTodoRules
{
    public static Result<ImmutableArray<object>, string> Remove(Session session, RemoveTodo command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Bind(session => TodoRules.CheckExists(session, command.TodoId))
            .Map<ImmutableArray<object>>(todo => [new TodoRemoved(session.Id, todo.Id, user, now)]);
}
