using Marten;
using Microsoft.Extensions.Options;
using Tuigether.Server.Shared;
using Wolverine;
using Wolverine.Persistence;

namespace Tuigether.Server.Presence.Disconnect;

/// <summary>
/// The hub sends it when a connection closes, also after a network drop that SignalR notices.
/// </summary>
public sealed record ClientDisconnected(string ConnectionId);

public static class ClientDisconnectedHandler
{
    public static Task<ConnectedClient?> LoadAsync(ClientDisconnected disconnected, IQuerySession query, CancellationToken cancellation) =>
        query.LoadAsync<ConnectedClient>(disconnected.ConnectionId, cancellation);

    public static (IStorageAction<ConnectedClient>, DeliveryMessage<CheckParticipantLeft>?) Handle(
        ClientDisconnected _,
        ConnectedClient? client,
        IOptions<ServerOptions> options) =>
        ClosedClient.Of(client, options.Value.LeaveGracePeriod);
}
