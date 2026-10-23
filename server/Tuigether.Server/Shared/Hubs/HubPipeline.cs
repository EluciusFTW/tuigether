using Tuigether.Contracts;
using Wolverine.SignalR;

namespace Tuigether.Server.Shared.Hubs;

public static class HubPipeline
{
    extension(WebApplication app)
    {
        public HubEndpointConventionBuilder MapTuigetherHub() => app.MapWolverineSignalRHub<TuigetherHub>(HubProtocol.Path);
    }
}
