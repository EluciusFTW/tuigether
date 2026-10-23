using Tuigether.Server.Driving.DriveHistoryView;
using Tuigether.Server.Driving.TimerView;

namespace Tuigether.Server.Driving;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDriving() => services.AddDrivingTimerView().AddDrivingDriveHistoryView();
    }
}
