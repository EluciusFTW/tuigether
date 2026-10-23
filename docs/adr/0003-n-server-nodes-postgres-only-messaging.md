---
status: accepted
---

# Several server nodes on one database, with PostgreSQL as the only messaging infrastructure

The hosting of the sync server is not decided. It can be one hosted server, or one server per developer laptop
against a shared database in the SINC Kubernetes cluster. The server therefore runs as N nodes on one PostgreSQL
database, without RabbitMQ, Kafka or Redis. A node pushes snapshots only to its own SignalR Clients. After an append,
the handler sends `SessionChanged` through Wolverine `BroadcastToAllNodesAsync`. Each node then loads the snapshots of
that Session and pushes them to its Clients.

## Considered Options

- **Plain `pg_notify` in the append transaction.** Latency in milliseconds. We chose the Wolverine broadcast to learn
  Wolverine messaging on PostgreSQL. `pg_notify` is the fallback if the latency hurts.
- **One server node.** Simpler, but a server per laptop then cannot reach the Clients on other laptops.

## Consequences

- The broadcast goes through the Wolverine control queue. It arrives after 1–5 s, and it is not durable. Snapshots
  are idempotent, so the next push or a reconnect heals a lost signal.
- Wolverine tracked sessions do not see the broadcast. Cross-node tests wait on a SignalR Client.
- Presence lives in the database, with a heartbeat per node. A sweep removes the Clients of a dead node.
- Durable scheduled messages end Drives and Breaks, and exactly one node runs each of them.
- All nodes on one database run the same contracts version. A node checks this at startup.
- With one server per laptop, every laptop holds database credentials and joins the Wolverine leader election.
