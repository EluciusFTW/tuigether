using System.Globalization;
using Alba;

namespace Tuigether.Server.Specs;

/// <summary>
/// The base of every spec that sends a Command over HTTP. It clears the database before each spec.
/// </summary>
public abstract class ServerSpec(ServerFixture server) : WolverineHttpSpec(server.Host), IAsyncLifetime
{
    protected static string CurrentProtocol => Protocol.Version.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The headers of a Client of the user <paramref name="userName"/> with the right API key and protocol version.
    /// </summary>
    protected static Action<Scenario> As(string userName) => Headers(ServerFixture.ApiKey, CurrentProtocol, userName);

    protected static Action<Scenario> Headers(string apiKey, string protocolVersion, string userName) =>
        scenario =>
        {
            scenario.WithRequestHeader(RequestHeaders.ApiKey, apiKey);
            scenario.WithRequestHeader(RequestHeaders.ProtocolVersion, protocolVersion);
            scenario.WithRequestHeader(RequestHeaders.UserName, userName);
        };

    public async ValueTask InitializeAsync() => await ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
