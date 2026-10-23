using Marten.Events.Aggregation;
using Tuigether.Contracts;

namespace Tuigether.Server.Driving.DriveHistoryView;

public sealed partial class SessionDriveHistoryProjection : SingleStreamProjection<SessionDriveHistory, Guid>
{
    public static SessionDriveHistory Create(SessionCreated created) => new(created.SessionId);

    public static SessionDriveHistory Apply(DriveStarted started, SessionDriveHistory history) =>
        Add(history, new(DriveHistoryKind.Started, started.Driver, started.User, started.At));

    public static SessionDriveHistory Apply(DrivePaused paused, SessionDriveHistory history) =>
        Add(history, new(DriveHistoryKind.Paused, CurrentDriver(history), paused.User, paused.At));

    public static SessionDriveHistory Apply(DriveResumed resumed, SessionDriveHistory history) =>
        Add(history, new(DriveHistoryKind.Resumed, CurrentDriver(history), resumed.User, resumed.At));

    public static SessionDriveHistory Apply(DriveSwitched switched, SessionDriveHistory history) =>
        Add(history, new(DriveHistoryKind.Switched, CurrentDriver(history), switched.User, switched.At));

    // No Participant causes the end of a Drive. The entry names the Driver, as the Firebase backend does.
    public static SessionDriveHistory Apply(DriveFinished finished, SessionDriveHistory history) =>
        Add(history, new(DriveHistoryKind.Finished, CurrentDriver(history), CurrentDriver(history), finished.At));

    public static SessionDriveHistory Apply(DriveSkipped skipped, SessionDriveHistory history) =>
        Add(history, new(DriveHistoryKind.Skipped, CurrentDriver(history), skipped.User, skipped.At));

    public static bool ShouldDelete(SessionDeleted _) => true;

    private static SessionDriveHistory Add(SessionDriveHistory history, DriveHistoryEntry entry) =>
        history with { Entries = [.. history.Entries, entry] };

    // A Drive belongs to the Driver who started it, also after the Driver left.
    private static string CurrentDriver(SessionDriveHistory history) =>
        history.Entries.LastOrDefault(static entry => entry.Kind == DriveHistoryKind.Started)?.Driver ?? "";
}
