using JasperFx.Events.Projections;
using Marten;

namespace Tuigether.Server.Domain;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDomain() =>
            services.ConfigureMarten(static options => options.Projections.Snapshot<Session>(SnapshotLifecycle.Inline));
    }
}
