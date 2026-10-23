namespace Tuigether.Contracts;

public static class Protocol
{
    /// <summary>
    /// Raise it on a breaking change of a contract. An additive change keeps it.
    /// </summary>
    public const int Version = 1;
}

public static class RequestHeaders
{
    public const string ApiKey = "X-Api-Key";

    public const string UserName = "X-User-Name";

    public const string ProtocolVersion = "X-Protocol-Version";
}
