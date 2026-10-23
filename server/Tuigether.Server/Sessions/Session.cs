using Tuigether.Contracts;

namespace Tuigether.Server.Sessions;

/// <summary>
/// The write model of one Session stream. The Command endpoints decide against it.
/// </summary>
public sealed record Session(Guid Id, bool IsDeleted)
{
    public static Session Create(SessionCreated created) => new(created.SessionId, IsDeleted: false);

    public static Session Apply(SessionDeleted _, Session session) => session with { IsDeleted = true };
}
