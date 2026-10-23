namespace Tuigether.Contracts;

/// <summary>
/// The HTTP routes of the Commands. Each Command is a POST with the Command as its JSON body.
/// </summary>
public static class CommandRoutes
{
    public const string CreateSession = "/api/sessions";

    public const string DeleteSession = "/api/sessions/delete";

    public const string RotateDriver = "/api/sessions/rotate-driver";

    public const string PauseDrive = "/api/sessions/pause-drive";

    public const string ResumeDrive = "/api/sessions/resume-drive";

    public const string SkipDrive = "/api/sessions/skip-drive";

    public const string SkipBreak = "/api/sessions/skip-break";

    public const string AcquireNotesEditLock = "/api/sessions/acquire-notes-edit-lock";

    public const string EndNotesEditing = "/api/sessions/end-notes-editing";
}

public sealed record CreateSession(string Title, string Repository, string Branch);

public sealed record CreateSessionResponse(Guid SessionId);

public sealed record DeleteSession(Guid SessionId);

/// <summary>
/// Makes <paramref name="NextDriver"/> the Driver and starts a Drive. It ends a running or paused Drive.
/// </summary>
/// <param name="CurrentDriver">The Driver that the Client shows, or null for no Driver. The server rejects the
/// Command when the Driver changed, so two Clients that rotate at the same moment give one rotation.</param>
/// <param name="NextDriver">A present Participant.</param>
/// <param name="Duration">The length of the new Drive.</param>
public sealed record RotateDriver(Guid SessionId, string? CurrentDriver, string NextDriver, TimeSpan Duration);

public sealed record PauseDrive(Guid SessionId);

public sealed record ResumeDrive(Guid SessionId);

/// <summary>
/// Ends the running Drive before its end and starts the Break.
/// </summary>
public sealed record SkipDrive(Guid SessionId);

public sealed record SkipBreak(Guid SessionId);

/// <summary>
/// Gives the caller the Edit lock of the Notes. Only the holder of the Edit lock edits the Notes.
/// </summary>
public sealed record AcquireNotesEditLock(Guid SessionId);

/// <summary>
/// Stores the Notes text of the Edit lock holder and releases the Edit lock.
/// </summary>
public sealed record EndNotesEditing(Guid SessionId, string Text);
