using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Sessions.ListView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSessionsListView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionSummaryProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options =>
                {
                    options.PublishMessage<SessionSummary>().ToSignalR();
                    options.PublishMessage<SessionRemoved>().ToSignalR();
                });
    }
}
