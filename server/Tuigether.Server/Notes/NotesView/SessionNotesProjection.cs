using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Notes.NotesView;

public sealed partial class SessionNotesProjection : SingleStreamProjection<SessionNotes, Guid>
{
    public static SessionNotes Create(SessionCreated created) => new(created.SessionId, Text: "", EditLockHolder: null);

    public static SessionNotes Apply(NotesEditingStarted started, SessionNotes notes) => notes with { EditLockHolder = started.User };

    public static SessionNotes Apply(NotesEdited edited, SessionNotes notes) => notes with { Text = edited.Text };

    public static SessionNotes Apply(NotesEditingEnded _, SessionNotes notes) => notes with { EditLockHolder = null };

    public static bool ShouldDelete(SessionDeleted _) => true;
}
