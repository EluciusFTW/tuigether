using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Todos.Add;

public static class AddTodoRules
{
    public static Result<ImmutableArray<object>, string> Add(Session session, AddTodo command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session =>
            session.Todos.Any(todo => todo.Id == command.TodoId)
                ? Result.Error("A todo with this id exists.")
                : Result.Ok<ImmutableArray<object>>([new TodoAdded(session.Id, command.TodoId, command.Text, user, now)]));
}
