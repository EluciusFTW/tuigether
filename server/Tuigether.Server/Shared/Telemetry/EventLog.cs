using Marten;
using Marten.Services;

namespace Tuigether.Server.Shared.Telemetry;

/// <summary>
/// Logs each event that a session commits, with its values. The values hold personal data, so it logs at Debug.
/// </summary>
public sealed class EventLog(ILogger<EventLog> logger) : DocumentSessionListenerBase, IConfigureMarten
{
    public void Configure(IServiceProvider services, StoreOptions options) => options.Listeners.Add(this);

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        foreach (var appended in commit.GetEvents())
        {
            logger.LogDebug(
                "Appended {EventTypeName} to stream {StreamId} at version {Version}: {Event}",
                appended.EventTypeName,
                appended.StreamId,
                appended.Version,
                appended.Data);
        }

        return Task.CompletedTask;
    }
}
