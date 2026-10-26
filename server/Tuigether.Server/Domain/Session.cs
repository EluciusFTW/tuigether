using System.Collections.Immutable;
using Tuigether.Contracts;

namespace Tuigether.Server.Domain;

/// <summary>
/// The write model of one Session stream. The Command handlers and the presence handlers decide against it.
/// </summary>
public sealed record Session(Guid Id, bool IsDeleted)
{
    public SessionStatus Status { get; init; } = SessionStatus.Created;

    /// <summary>
    /// The user names of the Participants, in the order of their join.
    /// </summary>
    public ImmutableArray<string> Participants { get; init; } = [];

    public string? Driver { get; init; }

    public TimerPhase Timer { get; init; } = TimerPhase.Idle;

    /// <summary>
    /// When the running Drive ends. Null when no Drive runs.
    /// </summary>
    public DateTimeOffset? DriveEndsAt { get; init; }

    /// <summary>
    /// The time that the paused Drive has left.
    /// </summary>
    public TimeSpan DriveRemaining { get; init; }

    /// <summary>
    /// When the Break ends. Null when no Break runs.
    /// </summary>
    public DateTimeOffset? BreakEndsAt { get; init; }

    public string NotesText { get; init; } = "";

    /// <summary>
    /// The Participant who holds the Edit lock of the Notes. Null when the Edit lock is free.
    /// </summary>
    public string? NotesEditLockHolder { get; init; }

    public string GoalText { get; init; } = "";

    /// <summary>
    /// The Participant who holds the Edit lock of the Goal. Null when the Edit lock is free.
    /// </summary>
    public string? GoalEditLockHolder { get; init; }

    /// <summary>
    /// In the order of <see cref="SessionTodos.Items"/>.
    /// </summary>
    public ImmutableArray<Todo> Todos { get; init; } = [];

    /// <summary>
    /// The ids of the note-list items. The text is not part of any decision, so the write model leaves it out.
    /// </summary>
    public ImmutableArray<string> NoteIds { get; init; } = [];

    public static Session Create(SessionCreated created) => new(created.SessionId, IsDeleted: false);

    public static Session Apply(SessionDeleted _, Session session) => session with { IsDeleted = true };

    public static Session Apply(SessionStarted _, Session session) => session with { Status = SessionStatus.Started };

    public static Session Apply(SessionFinished _, Session session) => session with { Status = SessionStatus.Finished };

    public static Session Apply(ParticipantJoined joined, Session session) =>
        session with { Participants = [.. session.Participants, joined.User] };

    public static Session Apply(ParticipantLeft left, Session session) =>
        session with { Participants = session.Participants.Remove(left.User) };

    public static Session Apply(DriverChanged changed, Session session) => session with { Driver = changed.Driver };

    public static Session Apply(DriveStarted started, Session session) =>
        session with { Timer = TimerPhase.DriveRunning, DriveEndsAt = started.EndsAt };

    public static Session Apply(DrivePaused paused, Session session) =>
        session with { Timer = TimerPhase.DrivePaused, DriveEndsAt = null, DriveRemaining = paused.Remaining };

    public static Session Apply(DriveResumed resumed, Session session) =>
        session with { Timer = TimerPhase.DriveRunning, DriveEndsAt = resumed.EndsAt };

    public static Session Apply(DriveFinished _, Session session) => Idle(session);

    public static Session Apply(DriveSkipped _, Session session) => Idle(session);

    public static Session Apply(BreakStarted started, Session session) =>
        Idle(session) with { Timer = TimerPhase.Break, BreakEndsAt = started.EndsAt };

    public static Session Apply(BreakSkipped _, Session session) => Idle(session);

    public static Session Apply(BreakEnded _, Session session) => Idle(session);

    public static Session Apply(NotesEditingStarted started, Session session) =>
        session with { NotesEditLockHolder = started.User };

    public static Session Apply(NotesEdited edited, Session session) => session with { NotesText = edited.Text };

    public static Session Apply(NotesEditingEnded _, Session session) => session with { NotesEditLockHolder = null };

    public static Session Apply(GoalEditingStarted started, Session session) =>
        session with { GoalEditLockHolder = started.User };

    public static Session Apply(GoalChanged changed, Session session) => session with { GoalText = changed.Text };

    public static Session Apply(GoalEditingEnded _, Session session) => session with { GoalEditLockHolder = null };

    public static Session Apply(TodoAdded added, Session session) =>
        session with { Todos = [.. session.Todos, new Todo(added.TodoId, Completed: false)] };

    public static Session Apply(TodoCompleted completed, Session session) => WithCompleted(session, completed.TodoId, completed: true);

    public static Session Apply(TodoReopened reopened, Session session) => WithCompleted(session, reopened.TodoId, completed: false);

    public static Session Apply(TodoMoved moved, Session session) =>
        session.Todos.FirstOrDefault(todo => todo.Id == moved.TodoId) is { } todo
            ? session with { Todos = session.Todos.Remove(todo).Insert(moved.NewPosition, todo) }
            : session;

    public static Session Apply(TodoRemoved removed, Session session) =>
        session with { Todos = session.Todos.RemoveAll(todo => todo.Id == removed.TodoId) };

    public static Session Apply(NoteAdded added, Session session) => session with { NoteIds = [.. session.NoteIds, added.NoteId] };

    public static Session Apply(NoteRemoved removed, Session session) => session with { NoteIds = session.NoteIds.Remove(removed.NoteId) };

    private static Session WithCompleted(Session session, string todoId, bool completed) =>
        session with
        {
            Todos = [.. session.Todos.Select(todo => todo.Id == todoId ? todo with { Completed = completed } : todo)],
        };

    private static Session Idle(Session session) =>
        session with { Timer = TimerPhase.Idle, DriveEndsAt = null, DriveRemaining = TimeSpan.Zero, BreakEndsAt = null };
}

/// <summary>
/// One item of the todo list. The text is not part of any decision, so the write model leaves it out.
/// </summary>
public sealed record Todo(string Id, bool Completed);
