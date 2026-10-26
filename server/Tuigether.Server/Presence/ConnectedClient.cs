namespace Tuigether.Server.Presence;

/// <summary>
/// One Client that has a Session open on one node. It is a plain document and not an event, because an open
/// connection is a fact of the running nodes and not of the Session.
/// </summary>
/// <param name="Id">The SignalR connection id.</param>
public sealed record ConnectedClient(string Id, Guid NodeId, string User, Guid SessionId);
