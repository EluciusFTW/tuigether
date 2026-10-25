using Tuigether.Server.NoteList.NoteListView;

namespace Tuigether.Server.NoteList;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNoteList() => services.AddNoteListNoteListView();
    }
}
