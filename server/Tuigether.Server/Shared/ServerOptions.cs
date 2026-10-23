namespace Tuigether.Server.Shared;

public sealed record ServerOptions
{
    public const string Section = "Tuigether";

    /// <summary>
    /// The one key that every Client of the team sends.
    /// </summary>
    public string ApiKey { get; init; } = "";

    /// <summary>
    /// The time after a closed Client before its User leaves the Session. A Client that opens the Session again
    /// within this time keeps the User in the Session.
    /// </summary>
    public TimeSpan LeaveGracePeriod { get; init; } = TimeSpan.FromSeconds(10);
}

public static class ServerRoutes
{
    public const string Health = "/health";
}
