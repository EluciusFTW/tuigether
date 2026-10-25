using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tuigether.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<SessionStatus>))]
public enum SessionStatus
{
    Created,
    Started,
    Finished,
}

public sealed record SessionSummary(
    Guid Id,
    string Title,
    SessionStatus Status,
    string CreatedBy,
    string Repository,
    string Branch,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// The user names of the Participants, in the order of their join.
    /// </summary>
    public ImmutableArray<string> Participants { get; init; } = [];

    // Record equality compares an ImmutableArray by reference.
    public bool Equals(SessionSummary? other) =>
        other is not null
        && (Id, Title, Status, CreatedBy, Repository, Branch, CreatedAt)
            == (other.Id, other.Title, other.Status, other.CreatedBy, other.Repository, other.Branch, other.CreatedAt)
        && Participants.SequenceEqual(other.Participants);

    public override int GetHashCode() =>
        HashCode.Combine(Id, Title, Status, CreatedBy, Repository, Branch, CreatedAt, Participants.Length);
}

/// <summary>
/// The data of one opened Session.
/// </summary>
public sealed record SessionData(
    Guid Id,
    string Title,
    SessionStatus Status,
    string CreatedBy,
    string Repository,
    string Branch,
    DateTimeOffset CreatedAt,
    string? Driver);

public sealed record Participant(string User, string Avatar);

/// <summary>
/// The Participants of one Session, in the order of their join.
/// </summary>
public sealed record SessionParticipants(Guid Id)
{
    public ImmutableArray<Participant> Participants { get; init; } = [];

    // Record equality compares an ImmutableArray by reference.
    public bool Equals(SessionParticipants? other) =>
        other is not null && Id == other.Id && Participants.SequenceEqual(other.Participants);

    public override int GetHashCode() => HashCode.Combine(Id, Participants.Length);
}

[JsonConverter(typeof(JsonStringEnumConverter<TimerPhase>))]
public enum TimerPhase
{
    Idle,
    DriveRunning,
    DrivePaused,
    Break,
}

/// <summary>
/// The timer of one Session.
/// </summary>
/// <param name="Driver">A paused Drive can have no Driver.</param>
/// <param name="EndsAt">The time on the server clock when the running Drive or the Break ends. Null when neither
/// runs.</param>
/// <param name="Remaining">The time that the Drive or the Break has left: at <see cref="EndsAt"/> minus the time of
/// its start or resume while it runs, and the frozen value while the Drive is paused. Zero when the timer is
/// idle.</param>
public sealed record SessionTimer(Guid Id, TimerPhase Phase, string? Driver, DateTimeOffset? EndsAt, TimeSpan Remaining);

[JsonConverter(typeof(JsonStringEnumConverter<DriveHistoryKind>))]
public enum DriveHistoryKind
{
    Started,
    Paused,
    Resumed,
    Switched,
    Finished,
    Skipped,
}

/// <param name="Driver">The Driver of the Drive that the entry belongs to.</param>
/// <param name="User">The Participant who caused the entry.</param>
public sealed record DriveHistoryEntry(DriveHistoryKind Kind, string Driver, string User, DateTimeOffset At);

/// <summary>
/// Every Drive of one Session, in time order. A Drive starts with a <see cref="DriveHistoryKind.Started"/> entry, and
/// the next entry that is not a pause or a resume ends it.
/// </summary>
public sealed record SessionDriveHistory(Guid Id)
{
    public ImmutableArray<DriveHistoryEntry> Entries { get; init; } = [];

    // Record equality compares an ImmutableArray by reference.
    public bool Equals(SessionDriveHistory? other) =>
        other is not null && Id == other.Id && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode() => HashCode.Combine(Id, Entries.Length);
}

/// <summary>
/// The Notes of one Session.
/// </summary>
/// <param name="EditLockHolder">The Participant who edits the Notes, or null when the Edit lock is free.</param>
public sealed record SessionNotes(Guid Id, string Text, string? EditLockHolder);

/// <summary>
/// The Goal of one Session.
/// </summary>
/// <param name="EditLockHolder">The Participant who edits the Goal, or null when the Edit lock is free.</param>
public sealed record SessionGoal(Guid Id, string Text, string? EditLockHolder);

public sealed record TodoItem(string Id, string Text, bool Completed);

/// <summary>
/// The todo list of one Session, in the order that every Client shows.
/// </summary>
public sealed record SessionTodos(Guid Id)
{
    public ImmutableArray<TodoItem> Items { get; init; } = [];

    // Record equality compares an ImmutableArray by reference.
    public bool Equals(SessionTodos? other) =>
        other is not null && Id == other.Id && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(Id, Items.Length);
}

public sealed record NoteListItem(string Id, string Text);

/// <summary>
/// The note list of one Session, in the order that every Client shows.
/// </summary>
public sealed record SessionNoteList(Guid Id)
{
    public ImmutableArray<NoteListItem> Items { get; init; } = [];

    // Record equality compares an ImmutableArray by reference.
    public bool Equals(SessionNoteList? other) =>
        other is not null && Id == other.Id && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(Id, Items.Length);
}
