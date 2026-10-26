namespace Tuigether.Server.Sessions.Delete;

/// <summary>
/// Removes the personal data of a deleted Session, see <see cref="MaskDeletedSessionHandler"/>.
/// </summary>
public sealed record MaskDeletedSession(Guid SessionId);
