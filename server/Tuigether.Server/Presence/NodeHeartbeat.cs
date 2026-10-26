namespace Tuigether.Server.Presence;

/// <summary>
/// The last sign of life of one node. The sweep reads its age to find a crashed node.
/// </summary>
/// <param name="Id">The node id, the same as <see cref="ConnectedClient.NodeId"/>.</param>
public sealed record NodeHeartbeat(Guid Id, DateTimeOffset At);
