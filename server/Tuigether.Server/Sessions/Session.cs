using System.Collections.Immutable;
using Tuigether.Contracts;

namespace Tuigether.Server.Sessions;

/// <summary>
/// The write model of one Session stream. The Command endpoints and the presence handlers decide against it.
/// </summary>
public sealed record Session(Guid Id, bool IsDeleted)
{
    public SessionStatus Status { get; init; } = SessionStatus.Created;

    /// <summary>
    /// The user names of the Participants, in the order of their join.
    /// </summary>
    public ImmutableArray<string> Participants { get; init; } = [];

    public static Session Create(SessionCreated created) => new(created.SessionId, IsDeleted: false);

    public static Session Apply(SessionDeleted _, Session session) => session with { IsDeleted = true };

    public static Session Apply(SessionStarted _, Session session) => session with { Status = SessionStatus.Started };

    public static Session Apply(SessionFinished _, Session session) => session with { Status = SessionStatus.Finished };

    public static Session Apply(ParticipantJoined joined, Session session) =>
        session with { Participants = [.. session.Participants, joined.User] };

    public static Session Apply(ParticipantLeft left, Session session) =>
        session with { Participants = session.Participants.Remove(left.User) };
}
