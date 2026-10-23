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
    DateTimeOffset CreatedAt);

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
