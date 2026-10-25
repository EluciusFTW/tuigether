using Tuigether.Server.Goal.GoalView;

namespace Tuigether.Server.Goal;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddGoal() => services.AddGoalGoalView();
    }
}
