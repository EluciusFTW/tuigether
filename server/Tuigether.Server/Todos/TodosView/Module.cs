using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Todos.TodosView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddTodosTodosView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionTodosProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionTodos>().ToSignalR());
    }
}
