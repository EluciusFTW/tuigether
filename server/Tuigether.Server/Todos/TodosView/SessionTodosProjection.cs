using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Todos.TodosView;

public sealed partial class SessionTodosProjection : SingleStreamProjection<SessionTodos, Guid>
{
    public static SessionTodos Create(SessionCreated created) => new(created.SessionId);

    public static SessionTodos Apply(TodoAdded added, SessionTodos todos) =>
        todos with { Items = [.. todos.Items, new TodoItem(added.TodoId, added.Text, Completed: false)] };

    public static SessionTodos Apply(TodoTextChanged changed, SessionTodos todos) =>
        Change(todos, changed.TodoId, item => item with { Text = changed.Text });

    public static SessionTodos Apply(TodoCompleted completed, SessionTodos todos) =>
        Change(todos, completed.TodoId, static item => item with { Completed = true });

    public static SessionTodos Apply(TodoReopened reopened, SessionTodos todos) =>
        Change(todos, reopened.TodoId, static item => item with { Completed = false });

    public static SessionTodos Apply(TodoMoved moved, SessionTodos todos) =>
        todos.Items.FirstOrDefault(item => item.Id == moved.TodoId) is { } item
            ? todos with { Items = todos.Items.Remove(item).Insert(moved.NewPosition, item) }
            : todos;

    public static SessionTodos Apply(TodoRemoved removed, SessionTodos todos) =>
        todos with { Items = todos.Items.RemoveAll(item => item.Id == removed.TodoId) };

    public static bool ShouldDelete(SessionDeleted _) => true;

    private static SessionTodos Change(SessionTodos todos, string todoId, Func<TodoItem, TodoItem> change) =>
        todos with { Items = [.. todos.Items.Select(item => item.Id == todoId ? change(item) : item)] };
}
