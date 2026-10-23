using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Notes.EditingEnd;

public static class EndNotesEditingRules
{
    public static Result<ImmutableArray<object>, string> End(Session session, string text, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session => session switch
        {
            _ when session.NotesEditLockHolder != user => Result.Error($"{user} does not hold the Edit lock of the Notes."),
            _ when session.NotesText == text => Result.Ok<ImmutableArray<object>>([new NotesEditingEnded(session.Id, user, now)]),
            _ => Result.Ok<ImmutableArray<object>>(
                [new NotesEdited(session.Id, text, user, now), new NotesEditingEnded(session.Id, user, now)]),
        });
}
