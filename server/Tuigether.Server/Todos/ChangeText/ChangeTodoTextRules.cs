using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Todos.ChangeText;

public static class ChangeTodoTextRules
{
    public static Result<ImmutableArray<object>, string> Change(Session session, ChangeTodoText command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Bind(session => TodoRules.CheckExists(session, command.TodoId))
            .Map<ImmutableArray<object>>(todo => [new TodoTextChanged(session.Id, todo.Id, command.Text, user, now)]);
}
