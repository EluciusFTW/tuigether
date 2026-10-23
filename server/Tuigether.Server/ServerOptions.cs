namespace Tuigether.Server;

public sealed record ServerOptions
{
    public const string Section = "Tuigether";

    /// <summary>
    /// The one key that every Client of the team sends.
    /// </summary>
    public string ApiKey { get; init; } = "";
}

public static class ServerRoutes
{
    public const string Health = "/health";
}
