using Wolverine;

namespace Tuigether.Server.Shared.Push;

/// <summary>
/// The signal that the stream of a Session has new events. Every node gets it and pushes the new snapshots to its own
/// Clients.
/// </summary>
public sealed record SessionChanged(Guid SessionId);

/// <summary>
/// Sends <see cref="SessionChanged"/> to every node. A Command handler returns it after each append. The outbox
/// holds the broadcast until the append commits.
/// </summary>
public sealed record BroadcastSessionChanged(Guid SessionId) : ISideEffect
{
    public ValueTask ExecuteAsync(IMessageBus bus) => bus.BroadcastToAllNodesAsync(new SessionChanged(SessionId));
}
