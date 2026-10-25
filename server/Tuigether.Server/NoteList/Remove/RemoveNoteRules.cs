using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.NoteList.Remove;

public static class RemoveNoteRules
{
    public static Result<ImmutableArray<object>, string> Remove(Session session, RemoveNote command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Bind(session => NoteListRules.CheckExists(session, command.NoteId))
            .Map<ImmutableArray<object>>(noteId => [new NoteRemoved(session.Id, noteId, user, now)]);
}
