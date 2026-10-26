using Tuigether.Server.Shared;

namespace Tuigether.Server.Domain;

public static class SessionRules
{
    public const string DeletedReason = "The Session is deleted.";

    /// <summary>
    /// Every Command on an existing Session passes this rule first.
    /// </summary>
    public static Result<Session, string> CheckNotDeleted(Session session) =>
        session.IsDeleted
            ? Result.Error(DeletedReason)
            : Result.Ok(session);

    /// <summary>
    /// The time that a running Drive or Break has left at <paramref name="now"/>.
    /// </summary>
    public static TimeSpan TimeLeft(DateTimeOffset endsAt, DateTimeOffset now) =>
        endsAt > now ? endsAt - now : TimeSpan.Zero;
}
