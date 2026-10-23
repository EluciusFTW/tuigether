using Tuigether.Server.Shared.Hubs;
using Tuigether.Server.Shared.Startup;

namespace Tuigether.Server.Shared;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddShared() => services.AddSharedStartup().AddSharedHubs();
    }
}
