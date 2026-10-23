using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Sessions;

namespace Tuigether.Server.Presence;

public static class PresenceRules
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

    /// <summary>
    /// The events when the grace period after a closed Client of <paramref name="user"/> ends.
    /// </summary>
    /// <param name="hasOpenClient">Another Client of the User has the Session open.</param>
    public static ImmutableArray<object> Leave(Session session, string user, bool hasOpenClient, DateTimeOffset now) =>
        session switch
        {
            { IsDeleted: true } => [],
            _ when hasOpenClient || !session.Participants.Contains(user) => [],
            { Status: SessionStatus.Started, Participants.Length: 1 } =>
                [new ParticipantLeft(session.Id, user, now), new SessionFinished(session.Id, user, now)],
            _ => [new ParticipantLeft(session.Id, user, now)],
        };
}
