using Wolverine;
using Wolverine.Persistence;

namespace Tuigether.Server.Presence;

public static class ClosedClient
{
    /// <summary>
    /// Removes the document of a closed Client and schedules the leave check of its User. A Client that has no Session
    /// open needs neither.
    /// </summary>
    public static (IStorageAction<ConnectedClient>, DeliveryMessage<CheckParticipantLeft>?) Of(ConnectedClient? client, TimeSpan gracePeriod) =>
        client is null
            ? (Storage.Nothing<ConnectedClient>(), null)
            : (Storage.Delete(client), new CheckParticipantLeft(client.SessionId, client.User).DelayedFor(gracePeriod));
}
