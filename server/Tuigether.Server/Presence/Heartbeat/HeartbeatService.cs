using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Options;
using Npgsql;
using Tuigether.Server.Shared;
using Wolverine.Runtime;

namespace Tuigether.Server.Presence.Heartbeat;

public sealed class HeartbeatService(
    IDocumentStore store,
    IWolverineRuntime runtime,
    TimeProvider clock,
    IOptions<ServerOptions> options,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.HeartbeatInterval);
        do
        {
            await BeatAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // An exception from ExecuteAsync stops the host. A missed beat is harmless until the heartbeat timeout.
    private async Task BeatAsync(CancellationToken cancellation)
    {
        try
        {
            await using var documents = store.LightweightSession();
            documents.Store(new NodeHeartbeat(runtime.Options.UniqueNodeId, clock.GetUtcNow()));
            await documents.SaveChangesAsync(cancellation);
        }
        catch (Exception error) when (error is NpgsqlException or MartenException)
        {
            logger.LogWarning(error, "The node {NodeId} missed a heartbeat.", runtime.Options.UniqueNodeId);
        }
    }
}
