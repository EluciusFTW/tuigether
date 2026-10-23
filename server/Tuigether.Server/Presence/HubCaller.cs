using System.Diagnostics;
using Wolverine;
using Wolverine.SignalR.Internals;

namespace Tuigether.Server.Presence;

/// <summary>
/// The SignalR connection that sent a message, with the user name of its connect.
/// </summary>
public sealed record HubCaller(string ConnectionId, string User)
{
    public static HubCaller Of(Envelope envelope) =>
        envelope switch
        {
            SignalREnvelope { UserName: { } user } signalR => new(signalR.ConnectionId, user),

            // TuigetherHub rejects a connect without a user name, and only a hub connection sends these messages.
            _ => throw new UnreachableException($"The message {envelope.MessageType} did not come from a hub connection."),
        };
}
