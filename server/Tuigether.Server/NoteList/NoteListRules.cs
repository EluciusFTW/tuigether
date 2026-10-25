using Tuigether.Server.Domain;
using Tuigether.Server.Shared;

namespace Tuigether.Server.NoteList;

public static class NoteListRules
{
    public static Result<string, string> CheckExists(Session session, string noteId) =>
        session.NoteIds.Contains(noteId)
            ? Result.Ok(noteId)
            : Result.Error("The note does not exist.");
}
