using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.NoteList.NoteListView;

public sealed partial class SessionNoteListProjection : SingleStreamProjection<SessionNoteList, Guid>
{
    public static SessionNoteList Create(SessionCreated created) => new(created.SessionId);

    public static SessionNoteList Apply(NoteAdded added, SessionNoteList noteList) =>
        noteList with { Items = [.. noteList.Items, new NoteListItem(added.NoteId, added.Text)] };

    public static SessionNoteList Apply(NoteChanged changed, SessionNoteList noteList) =>
        noteList with
        {
            Items = [.. noteList.Items.Select(item => item.Id == changed.NoteId ? item with { Text = changed.Text } : item)],
        };

    public static SessionNoteList Apply(NoteRemoved removed, SessionNoteList noteList) =>
        noteList with { Items = noteList.Items.RemoveAll(item => item.Id == removed.NoteId) };

    public static bool ShouldDelete(SessionDeleted _) => true;
}
