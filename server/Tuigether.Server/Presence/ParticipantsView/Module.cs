using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Presence.ParticipantsView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPresenceParticipantsView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionParticipantsProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionParticipants>().ToSignalR());
    }
}
