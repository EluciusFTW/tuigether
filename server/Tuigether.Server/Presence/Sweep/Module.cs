using Wolverine;

namespace Tuigether.Server.Presence.Sweep;

public static class Module
{
    extension(IServiceCollection services)
    {
        // The schedule of the sweep is in Program.cs (docs/agents/csharp.md, "Recurring message in a module").
        public IServiceCollection AddPresenceSweep() =>
            services.ConfigureWolverine(static options =>
                options.PublishMessage<SweepCrashedNodes>().ToLocalQueue("presence-sweep").UseDurableInbox());
    }
}
