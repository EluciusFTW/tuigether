using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Todos.Reopen;

public static class ReopenTodoRules
{
    public static Result<ImmutableArray<object>, string> Reopen(Session session, ReopenTodo command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Bind(session => TodoRules.CheckExists(session, command.TodoId))
            .Bind<ImmutableArray<object>>(todo => todo.Completed
                ? Result.Ok<ImmutableArray<object>>([new TodoReopened(session.Id, todo.Id, user, now)])
                : Result.Error("The todo is open already."));
}
