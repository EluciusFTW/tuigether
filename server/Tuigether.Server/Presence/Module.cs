using Tuigether.Server.Presence.Heartbeat;
using Tuigether.Server.Presence.ParticipantsView;
using Tuigether.Server.Presence.Sweep;
using Wolverine;

namespace Tuigether.Server.Presence;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPresence() =>
            services
                // A durable queue keeps a scheduled leave check across a node restart.
                .ConfigureWolverine(static options =>
                    options.PublishMessage<CheckParticipantLeft>().ToLocalQueue("presence").UseDurableInbox())
                .AddPresenceHeartbeat()
                .AddPresenceSweep()
                .AddPresenceParticipantsView();
    }
}
