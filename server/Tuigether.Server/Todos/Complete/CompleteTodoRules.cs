using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Todos.Complete;

public static class CompleteTodoRules
{
    public static Result<ImmutableArray<object>, string> Complete(Session session, CompleteTodo command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Bind(session => TodoRules.CheckExists(session, command.TodoId))
            .Bind<ImmutableArray<object>>(todo => todo.Completed
                ? Result.Error("The todo is completed already.")
                : Result.Ok<ImmutableArray<object>>([new TodoCompleted(session.Id, todo.Id, user, now)]));
}
