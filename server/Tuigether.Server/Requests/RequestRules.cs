using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tuigether.Contracts;

namespace Tuigether.Server.Requests;

public abstract record RequestRejection
{
    private RequestRejection() { }

    public sealed record WrongApiKey : RequestRejection;

    public sealed record IncompatibleProtocol(string? ClientVersion) : RequestRejection;

    public sealed record MissingUserName : RequestRejection;
}

/// <summary>
/// The headers that <see cref="RequestRules.Check"/> checks.
/// </summary>
public sealed record RequestHeaderValues(string? ApiKey, string? ProtocolVersion, string? UserName)
{
    public static RequestHeaderValues Read(IHeaderDictionary? headers) =>
        new(
            headers?[RequestHeaders.ApiKey].FirstOrDefault(),
            headers?[RequestHeaders.ProtocolVersion].FirstOrDefault(),
            headers?[RequestHeaders.UserName].FirstOrDefault());
}

public static class RequestRules
{
    private static readonly string ServerProtocolVersion = Protocol.Version.ToString(CultureInfo.InvariantCulture);

    public static Result<RequestHeaderValues, RequestRejection> CheckApiKey(RequestHeaderValues headers, string serverApiKey) =>
        HasApiKey(headers.ApiKey, serverApiKey)
            ? Result.Ok(headers)
            : Result.Error<RequestRejection>(new RequestRejection.WrongApiKey());

    /// <returns>The user name of the caller.</returns>
    public static Result<string, RequestRejection> Check(RequestHeaderValues headers, string serverApiKey) =>
        CheckApiKey(headers, serverApiKey).Bind<string>(static headers =>
            headers switch
            {
                { ProtocolVersion: var version } when version != ServerProtocolVersion =>
                    Result.Error<RequestRejection>(new RequestRejection.IncompatibleProtocol(version)),
                { UserName: { } userName } when !string.IsNullOrWhiteSpace(userName) => Result.Ok(userName),
                _ => Result.Error<RequestRejection>(new RequestRejection.MissingUserName()),
            });

    public static (int Status, string Reason) Describe(RequestRejection rejection) =>
        rejection switch
        {
            RequestRejection.WrongApiKey => (StatusCodes.Status401Unauthorized, "The API key is wrong."),
            RequestRejection.IncompatibleProtocol { ClientVersion: var client } => (
                StatusCodes.Status400BadRequest,
                $"This tuigether uses protocol version {client ?? "none"}, and the server uses version {Protocol.Version}. Update tuigether."),
            RequestRejection.MissingUserName => (StatusCodes.Status400BadRequest, "The request has no user name."),
            _ => throw new UnreachableException(),
        };

    public static ConnectRejection ToConnectRejection(RequestRejection rejection) =>
        rejection switch
        {
            RequestRejection.WrongApiKey => ConnectRejection.WrongApiKey,
            RequestRejection.IncompatibleProtocol => ConnectRejection.IncompatibleProtocol,
            RequestRejection.MissingUserName => ConnectRejection.MissingUserName,
            _ => throw new UnreachableException(),
        };

    // A comparison in constant time does not tell an attacker how many leading characters match.
    private static bool HasApiKey(string? apiKey, string serverApiKey) =>
        apiKey is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(serverApiKey));
}
