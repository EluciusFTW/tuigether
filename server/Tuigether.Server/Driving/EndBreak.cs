namespace Tuigether.Server.Driving;

/// <summary>
/// Runs at <paramref name="EndsAt"/> of a Break. <see cref="DrivingRules.ScheduleEnds"/> schedules it.
/// </summary>
/// <param name="EndsAt">The end that the message was scheduled for. A skip ends the Break, so a message for a Break
/// that already ended does nothing.</param>
public sealed record EndBreak(Guid SessionId, DateTimeOffset EndsAt);
