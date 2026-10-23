using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Driving.TimerView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDrivingTimerView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionTimerProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionTimer>().ToSignalR());
    }
}
