using Tuigether.Server.Notes.NotesView;

namespace Tuigether.Server.Notes;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNotes() => services.AddNotesNotesView();
    }
}
