namespace Tuigether.Server.Presence;

/// <summary>
/// Runs one grace period after a Client of <paramref name="User"/> closed the Session, and at once after the sweep
/// removed a Client of <paramref name="User"/> on a crashed node.
/// </summary>
public sealed record CheckParticipantLeft(Guid SessionId, string User);
