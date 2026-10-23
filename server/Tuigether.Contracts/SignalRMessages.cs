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

/// <summary>
/// Opens a Session for this connection. The server answers with all snapshots of the Session, and then it pushes
/// each snapshot that changes. The first open Client of a User makes the User a Participant.
/// </summary>
/// <param name="Avatar">The avatar of the User on the journey panel.</param>
public sealed record OpenSession(Guid SessionId, string Avatar);

/// <summary>
/// Closes the Session for this connection. A closed connection also closes its Session. The User leaves the Session
/// after the grace period of the server, when no other Client of the User has the Session open.
/// </summary>
public sealed record CloseSession(Guid SessionId);
