using Tuigether.Server.Todos.TodosView;

namespace Tuigether.Server.Todos;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddTodos() => services.AddTodosTodosView();
    }
}
