using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Driving.BreakSkip;

public static class SkipBreakRules
{
    public static Result<ImmutableArray<object>, string> Skip(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            { Timer: TimerPhase.Break } => Result.Ok<ImmutableArray<object>>([new BreakSkipped(session.Id, user, now)]),
            _ => Result.Error("No Break runs."),
        });
}
