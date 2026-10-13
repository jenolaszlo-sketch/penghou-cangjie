# ADR 001 — Bounded current-store compatibility boundary

Status: accepted for 1.0 graduation. No prior ADR is superseded; the semantic
store contract is preserved with explicit limits and ownership.

Cangjie already has one complete implemented store interface and immutable
physical revisions. A general paging engine or retention framework would enlarge
this graduation. We freeze all existing signatures, add fixed defensive bounds,
fail complete collection reads on overflow, and retain underlying histories.
Snapshots are bounded pinned ordered selections; consumer-side indices traverse
that one selection, while repeated searches are independent new selections.

Immutable contracts copy caller collections into read-only containers. This
settles ownership mechanically while preserving object-initializer signatures.
Reference equality of collection properties remains .NET record behavior; retry
identity is compared explicitly. Options are captured at provider construction.

Schema 4 remains the persistence contract. Incompatible schemas fail without
automatic migration or deletion. Retention mechanics protect history and pins;
application quotas, scheduling and migration are separate concerns.

1.x freezes assembly identity and public signatures. Future optional capabilities
use separate interfaces rather than new abstract IContextStore members. New
required capabilities or altered persisted meanings require a major version.
The alternatives (unbounded materialization, automatic pruning, new cursors or
adding catch-all extension members) were rejected because they either create
unsafe permanent promises or initiate a new feature cycle.
