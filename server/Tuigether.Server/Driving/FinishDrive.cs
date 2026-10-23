namespace Tuigether.Server.Driving;

/// <summary>
/// Runs at <paramref name="EndsAt"/> of a running Drive. <see cref="DrivingRules.ScheduleEnds"/> schedules it.
/// </summary>
/// <param name="EndsAt">The end that the message was scheduled for. A pause, a skip or a switch changes the end of
/// the Drive, so a message with another end does nothing.</param>
public sealed record FinishDrive(Guid SessionId, DateTimeOffset EndsAt);
