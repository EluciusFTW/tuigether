using JasperFx.Events.Projections;
using Marten;
using Tuigether.Contracts;
using Wolverine;
using Wolverine.SignalR;

namespace Tuigether.Server.Driving.DriveHistoryView;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDrivingDriveHistoryView() =>
            services
                .ConfigureMarten(static options => options.Projections.Add<SessionDriveHistoryProjection>(ProjectionLifecycle.Inline))
                .ConfigureWolverine(static options => options.PublishMessage<SessionDriveHistory>().ToSignalR());
    }
}
