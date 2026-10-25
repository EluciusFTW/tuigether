using System.Collections.Immutable;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.NoteList.Add;

public static class AddNoteRules
{
    public static Result<ImmutableArray<object>, string> Add(Session session, AddNote command, string user, DateTimeOffset now) =>
        SessionRules.CheckNotDeleted(session).Bind<ImmutableArray<object>>(session =>
            session.NoteIds.Contains(command.NoteId)
                ? Result.Error("A note with this id exists.")
                : Result.Ok<ImmutableArray<object>>([new NoteAdded(session.Id, command.NoteId, command.Text, user, now)]));
}
