using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.NoteList.Change;

public static class ChangeNoteRules
{
    public static Result<ImmutableArray<object>, string> Change(Session session, ChangeNote command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session)
            .Bind(session => NoteListRules.CheckExists(session, command.NoteId))
            .Map<ImmutableArray<object>>(noteId => [new NoteChanged(session.Id, noteId, command.Text, user, now)]);
}
