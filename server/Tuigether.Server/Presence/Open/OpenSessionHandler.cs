using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Hubs;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Persistence;
using Wolverine.Runtime;
using Wolverine.SignalR;

namespace Tuigether.Server.Presence.Open;

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
        var now = clock.GetUtcNow();

        // A User with another open Client is already a Participant. A Finished Session stays Finished.
        Events events = session switch
        {
            _ when session.Participants.Contains(caller.User) => [],
            { Status: SessionStatus.Created } =>
                [new ParticipantJoined(session.Id, caller.User, command.Avatar, now), new SessionStarted(session.Id, caller.User, now)],
            _ => [new ParticipantJoined(session.Id, caller.User, command.Avatar, now)],
        };

        return (
            events,
            Storage.Store(new ConnectedClient(caller.ConnectionId, runtime.Options.UniqueNodeId, caller.User, session.Id)),
            new AddConnectionToGroup(TuigetherHub.SessionGroup(session.Id)),
            new SendSessionSnapshots(session.Id, caller.ConnectionId),
            events.Count == 0 ? null : new BroadcastSessionChanged(session.Id));
    }
}
