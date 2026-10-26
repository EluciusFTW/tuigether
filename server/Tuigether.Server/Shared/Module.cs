using Tuigether.Server.Shared.Hubs;
using Tuigether.Server.Shared.Masking;
using Tuigether.Server.Shared.Startup;
using Tuigether.Server.Shared.Telemetry;

namespace Tuigether.Server.Shared;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddShared(IConfiguration configuration) =>
            services.AddSharedStartup().AddSharedHubs().AddSharedMasking().AddSharedTelemetry(configuration);
    }
}
