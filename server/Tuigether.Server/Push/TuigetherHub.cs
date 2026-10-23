using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Tuigether.Contracts;
using Tuigether.Server.Requests;
using Wolverine.SignalR;
using Wolverine.SignalR.Internals;

namespace Tuigether.Server.Push;

/// <summary>
/// The SignalR hub of a node. It checks the headers of each connect, as <see cref="RequestCheckMiddleware"/> does for
/// a Command. The middleware already rejects a connect with a wrong API key.
/// </summary>
public sealed class TuigetherHub(SignalRTransport endpoint, IOptions<ServerOptions> options) : WolverineHub(endpoint)
{
    public const string SessionListGroup = "session-list";

    public override async Task OnConnectedAsync()
    {
        var headers = RequestHeaderValues.Read(Context.GetHttpContext()?.Request.Headers);

        if (RequestRules.Check(headers, options.Value.ApiKey) is Result<string, RequestRejection>.Error { Value: var rejection })
        {
            var reason = RequestRules.Describe(rejection).Reason;
            await Clients.Caller.SendAsync(
                HubProtocol.ConnectRejected,
                new ConnectRejected(RequestRules.ToConnectRejection(rejection), reason));

            // SignalR sends the message of a HubException to the client when it closes the connection. An HTTP
            // rejection of the connect reaches the .NET SignalR client only as a status code.
            throw new HubException(reason);
        }

        await base.OnConnectedAsync();
    }
}
