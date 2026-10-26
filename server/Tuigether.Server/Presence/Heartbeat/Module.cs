namespace Tuigether.Server.Presence.Heartbeat;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPresenceHeartbeat() => services.AddHostedService<HeartbeatService>();
    }
}
