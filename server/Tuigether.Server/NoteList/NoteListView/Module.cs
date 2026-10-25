using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.NoteList.NoteListView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNoteListNoteListView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionNoteListProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionNoteList>().ToSignalR());
    }
}
