using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Goal.GoalView;

public sealed partial class SessionGoalProjection : SingleStreamProjection<SessionGoal, Guid>
{
    public static SessionGoal Create(SessionCreated created) => new(created.SessionId, Text: "", EditLockHolder: null);

    public static SessionGoal Apply(GoalEditingStarted started, SessionGoal goal) => goal with { EditLockHolder = started.User };

    public static SessionGoal Apply(GoalChanged edited, SessionGoal goal) => goal with { Text = edited.Text };

    public static SessionGoal Apply(GoalEditingEnded _, SessionGoal goal) => goal with { EditLockHolder = null };

    public static bool ShouldDelete(SessionDeleted _) => true;
}
