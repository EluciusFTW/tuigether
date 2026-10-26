using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Git.BranchChange;

public static class ChangeSessionBranchRules
{
    public static Result<ImmutableArray<object>, string> Change(Session session, ChangeSessionBranch command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Map<ImmutableArray<object>>(session => [new SessionBranchChanged(session.Id, command.Branch, user, now)]);
}
