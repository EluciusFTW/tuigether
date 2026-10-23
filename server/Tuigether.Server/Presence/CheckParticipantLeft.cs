namespace Tuigether.Server.Presence;

/// <summary>
/// Runs one grace period after a Client of <paramref name="User"/> closed the Session.
/// </summary>
public sealed record CheckParticipantLeft(Guid SessionId, string User);
