using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Todos;

public static class TodoRules
{
    public static Result<Todo, string> CheckExists(Session session, string todoId) =>
        session.Todos.FirstOrDefault(todo => todo.Id == todoId) is { } todo
            ? Result.Ok(todo)
            : Result.Error("The todo does not exist.");
}
