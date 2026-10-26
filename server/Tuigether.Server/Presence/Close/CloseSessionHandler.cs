using Marten;
using Microsoft.Extensions.Options;
using Tuigether.Contracts;
using Tuigether.Server.Shared;
using Tuigether.Server.Shared.Hubs;
using Wolverine;
using Wolverine.Persistence;
using Wolverine.SignalR;

namespace Tuigether.Server.Presence.Close;

public static class CloseSessionHandler
{
    public static Task<ConnectedClient?> LoadAsync(CloseSession _, Envelope envelope, IQuerySession query, CancellationToken cancellation) =>
        query.LoadAsync<ConnectedClient>(HubCaller.Of(envelope).ConnectionId, cancellation);

    public static (RemoveConnectionToGroup, IStorageAction<ConnectedClient>, DeliveryMessage<CheckParticipantLeft>?) Handle(
        CloseSession command,
        ConnectedClient? client,
        IOptions<ServerOptions> options)
    {
        var (delete, check) = ClosedClient.Of(client, options.Value.LeaveGracePeriod);
        return (new RemoveConnectionToGroup(TuigetherHub.SessionGroup(command.SessionId)), delete, check);
    }
}
