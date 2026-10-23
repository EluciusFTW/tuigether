using Microsoft.AspNetCore.SignalR;
using Tuigether.Server.Requests;

namespace Tuigether.Server.Presence;

/// <summary>
/// Makes the user name header of a connect the user identifier of the connection. Wolverine copies the identifier to
/// each message that the connection sends, see <see cref="HubCaller"/>.
/// </summary>
public sealed class UserNameHeaderProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        RequestHeaderValues.Read(connection.GetHttpContext()?.Request.Headers).UserName;
}
