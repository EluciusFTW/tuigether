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

    /// <summary>
    /// The length of the Break after a finished or skipped Drive.
    /// </summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The time between two heartbeats of one node.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The age of the last heartbeat after which a node counts as crashed. The sweep then removes its Clients.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public static class ServerRoutes
{
    public const string Health = "/health";
}
