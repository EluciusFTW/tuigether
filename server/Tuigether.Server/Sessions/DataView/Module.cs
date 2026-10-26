using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Sessions.DataView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSessionsDataView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionDataProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionData>().ToSignalR());
    }
}
