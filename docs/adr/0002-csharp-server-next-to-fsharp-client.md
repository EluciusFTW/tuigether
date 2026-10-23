---
status: accepted
---

# C# server next to the F# client

The tuigether client is F#. The sync server is C#, because Wolverine, Marten and Bobcat document C# first. Their
handler discovery, the Marten `Create`/`Apply` aggregate convention and the Bobcat source generator do not work for
F#. A C# contracts project holds the Commands, the domain events and the snapshots. The server and the F# client both
reference it, so the message type names match by construction. The contracts are flat records without nested unions,
because F# code matches nested C# records badly.
