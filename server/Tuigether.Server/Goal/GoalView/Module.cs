using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Goal.GoalView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddGoalGoalView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionGoalProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionGoal>().ToSignalR());
    }
}
