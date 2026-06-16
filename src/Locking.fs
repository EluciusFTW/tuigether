module Locking

// Single-writer editing lock shared by the goal (SessionInfo) and freetext notes
// (Notes). The holder owns the lock; others can't enter Insert until it's released.
// LockedAt is refreshed on every debounced save so a crashed holder's lock expires
// after the TTL. The time-based predicates live here; the model-specific glue
// (which InputMode counts as "holding") stays with each component.

type Lock = { Owner: string; LockedAt: int64 }

let private ttlMs = 60_000L

/// A lock counts as active (not expired) while it was last refreshed within the TTL.
let isActive (now: int64) (lock: Lock) = now - lock.LockedAt <= ttlMs

/// True when an active lock is held by someone other than `user`.
let heldByOther (now: int64) (user: string) (lock: Lock option) =
  match lock with
  | Some l when isActive now l -> l.Owner <> user
  | _ -> false
