using Tuigether.Server.Driving.DriveHistoryView;
using Tuigether.Server.Driving.TimerView;
using Wolverine;

namespace Tuigether.Server.Driving;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDriving() =>
            services
                // A durable queue keeps a scheduled end across a node restart.
                .ConfigureWolverine(static options =>
                {
                    options.PublishMessage<FinishDrive>().ToLocalQueue("driving").UseDurableInbox();
                    options.PublishMessage<EndBreak>().ToLocalQueue("driving").UseDurableInbox();
                })
                .AddDrivingTimerView()
                .AddDrivingDriveHistoryView();
    }
}
