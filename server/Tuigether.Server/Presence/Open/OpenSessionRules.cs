using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Presence.Open;

public static class OpenSessionRules
{
    /// <summary>
    /// The events when a Client of <paramref name="user"/> opens the Session. A User with another open Client is
    /// already a Participant. A Finished Session stays Finished.
    /// </summary>
    public static ImmutableArray<object> Join(Session session, string user, string avatar, DateTimeOffset now) =>
        session switch
        {
            { IsDeleted: true } => [],
            _ when session.Participants.Contains(user) => [],
            { Status: SessionStatus.Created } =>
                [new ParticipantJoined(session.Id, user, avatar, now), new SessionStarted(session.Id, user, now)],
            _ => [new ParticipantJoined(session.Id, user, avatar, now)],
        };
}
