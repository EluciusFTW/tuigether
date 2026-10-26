using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Git.WipPush;

public static class RecordWipPushRules
{
    public static Result<ImmutableArray<object>, string> Record(Session session, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Map<ImmutableArray<object>>(session => [new WipPushed(session.Id, user, now)]);
}
