using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Notes.NotesView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNotesNotesView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionNotesProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionNotes>().ToSignalR());
    }
}
