using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Sessions.ListView;

public sealed partial class SessionSummaryProjection : SingleStreamProjection<SessionSummary, Guid>
{
    public static SessionSummary Create(SessionCreated created) =>
        new(created.SessionId, created.Title, SessionStatus.Created, created.User, created.Repository, created.Branch, created.At);

    public static SessionSummary Apply(SessionStarted _, SessionSummary summary) => summary with { Status = SessionStatus.Started };

    public static SessionSummary Apply(SessionFinished _, SessionSummary summary) => summary with { Status = SessionStatus.Finished };

    public static SessionSummary Apply(ParticipantJoined joined, SessionSummary summary) =>
        summary with { Participants = [.. summary.Participants, joined.User] };

    public static SessionSummary Apply(ParticipantLeft left, SessionSummary summary) =>
        summary with { Participants = summary.Participants.Remove(left.User) };

    public static SessionSummary Apply(SessionBranchChanged changed, SessionSummary summary) => summary with { Branch = changed.Branch };

    public static SessionSummary Apply(WipPushed pushed, SessionSummary summary) =>
        summary with { LastWipPushBy = pushed.User, LastWipPushAt = pushed.At };

    public static bool ShouldDelete(SessionDeleted _) => true;
}
