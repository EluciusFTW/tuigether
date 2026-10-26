using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Presence.ParticipantsView;

public sealed partial class SessionParticipantsProjection : SingleStreamProjection<SessionParticipants, Guid>
{
    public static SessionParticipants Create(SessionCreated created) => new(created.SessionId);

    public static SessionParticipants Apply(ParticipantJoined joined, SessionParticipants participants) =>
        participants with { Participants = [.. participants.Participants, new Participant(joined.User, joined.Avatar)] };

    public static SessionParticipants Apply(ParticipantLeft left, SessionParticipants participants) =>
        participants with { Participants = participants.Participants.RemoveAll(participant => participant.User == left.User) };

    public static bool ShouldDelete(SessionDeleted _) => true;
}
