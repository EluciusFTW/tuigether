using Marten;
using Microsoft.Extensions.Options;
using Tuigether.Contracts;
using Tuigether.Server.Push;
using Tuigether.Server.Sessions;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Persistence;
using Wolverine.Runtime;
using Wolverine.SignalR;

namespace Tuigether.Server.Presence;

/// <summary>
/// The hub sends it when a connection closes, also after a network drop that SignalR notices.
/// </summary>
public sealed record ClientDisconnected(string ConnectionId);

/// <summary>
/// Runs one grace period after a Client of <paramref name="User"/> closed the Session.
/// </summary>
public sealed record CheckParticipantLeft(Guid SessionId, string User);

public static class OpenSessionHandler
{
    public static HandlerContinuation Validate(Session session) =>
        session.IsDeleted ? HandlerContinuation.Stop : HandlerContinuation.Continue;

    // The open and the leave check lock the stream, see CheckParticipantLeftHandler.
    public static (Events, IStorageAction<ConnectedClient>, AddConnectionToGroup, SendSessionSnapshots, BroadcastSessionChanged?) Handle(
        OpenSession command,
        [WriteAggregate(LoadStyle = ConcurrencyStyle.Exclusive)] Session session,
        Envelope envelope,
        IWolverineRuntime runtime,
        TimeProvider clock)
    {
        var caller = HubCaller.Of(envelope);
        var events = PresenceRules.Join(session, caller.User, command.Avatar, clock.GetUtcNow());

        return (
            [.. events],
            Storage.Store(new ConnectedClient(caller.ConnectionId, runtime.Options.UniqueNodeId, caller.User, session.Id)),
            new AddConnectionToGroup(TuigetherHub.SessionGroup(session.Id)),
            new SendSessionSnapshots(session.Id, caller.ConnectionId),
            events.IsEmpty ? null : new BroadcastSessionChanged(session.Id));
    }
}

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

public static class CheckParticipantLeftHandler
{
    // The exclusive lock on the stream makes an open wait for the check, or the check wait for the open. The query of
    // the open Clients runs after the lock, so it sees the Client of an open that committed first.
    // A check can outlive its Session, for example after a delete.
    public static async Task<(Events, BroadcastSessionChanged?)> HandleAsync(
        CheckParticipantLeft check,
        [WriteAggregate(Required = false, LoadStyle = ConcurrencyStyle.Exclusive)] Session? session,
        IDocumentSession documents,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        var hasOpenClient = await documents.Query<ConnectedClient>()
            .AnyAsync(client => client.SessionId == check.SessionId && client.User == check.User, cancellation);
        var events = session is null ? [] : PresenceRules.Leave(session, check.User, hasOpenClient, clock.GetUtcNow());
        return ([.. events], events.IsEmpty ? null : new BroadcastSessionChanged(check.SessionId));
    }
}

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
