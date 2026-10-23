namespace Tuigether.Server.Shared.Startup;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSharedStartup() => services.AddHostedService<ContractsVersionCheck>();
    }
}
