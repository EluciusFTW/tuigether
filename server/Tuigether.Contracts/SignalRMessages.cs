using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tuigether.Contracts;

/// <summary>
/// The SignalR hub of a node. A connect carries the same <see cref="RequestHeaders"/> as a Command.
/// </summary>
public static class HubProtocol
{
    public const string Path = "/api/hub";

    /// <summary>
    /// The hub method for every message in both directions. Its one argument is a CloudEvents JSON string: <c>type</c>
    /// is the full name of the contract type, and <c>data</c> is the message in camel case.
    /// </summary>
    public const string ReceiveMessage = "ReceiveMessage";

    /// <summary>
    /// The hub method for <see cref="Contracts.ConnectRejected"/>. Its argument is the record itself, without a
    /// CloudEvents wrapper.
    /// </summary>
    public const string ConnectRejected = "ConnectRejected";
}

[JsonConverter(typeof(JsonStringEnumConverter<ConnectRejection>))]
public enum ConnectRejection
{
    WrongApiKey,
    IncompatibleProtocol,
    MissingUserName,
}

/// <summary>
/// The node rejects the connect and closes the connection after this message. The Client must not reconnect. A
/// connect with a wrong API key gets HTTP 401 before the hub runs, and no such message.
/// </summary>
/// <param name="Detail">The reason for the User.</param>
public sealed record ConnectRejected(ConnectRejection Reason, string Detail);

/// <summary>
/// Joins the push group of the Session list. The server answers with <see cref="SessionList"/>, and then it pushes
/// each <see cref="SessionSummary"/> that changes and each <see cref="SessionRemoved"/>.
/// </summary>
public sealed record JoinSessionList;

/// <summary>
/// All current Sessions. It replaces the list of the Client.
/// </summary>
public sealed record SessionList
{
    public ImmutableArray<SessionSummary> Sessions { get; init; } = [];
}

public sealed record SessionRemoved(Guid SessionId);
