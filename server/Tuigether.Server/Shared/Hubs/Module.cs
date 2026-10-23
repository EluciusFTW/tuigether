using Microsoft.AspNetCore.SignalR;

namespace Tuigether.Server.Shared.Hubs;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSharedHubs() => services.AddSingleton<IUserIdProvider, UserNameHeaderProvider>();
    }
}
