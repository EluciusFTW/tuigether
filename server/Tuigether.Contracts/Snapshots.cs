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
    string Branch);
