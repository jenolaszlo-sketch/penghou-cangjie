# Cangjie 1.0 current store profile

This is the normative contract for core, Sqlite and Testing. Cangjie is a bounded
local attributable context store, not an unlimited general memory database.
The semantic rules in [store-contract](store-contract.md) also apply.

## Operations and limits

Every `IContextStore` member is intentional. Future optional capabilities use
separate interfaces; no new abstract interface members are added in 1.x.
No public experimental APIs ship in this profile. Diagnostic constants and
provider-neutral extensible kind strings are intentional stable APIs.

SQLite validates writes before opening their transaction. Limits are fixed:

| Input/output | Maximum |
|---|---|
| Content | 1,048,576 UTF-8 bytes per item |
| Batch | 100 requests; empty batches rejected |
| Snapshot | 1,000 distinct nonempty physical IDs; empty selection rejected |
| Metadata/provenance attributes (each) | 100 entries and 65,536 bytes of System.Text.Json UTF-8 JSON; each key/value additionally has the text limit |
| Other strings, including query text and metadata values | 4,096 UTF-16 code units |
| Tags, scopes, kind filters | 100 values |
| Search/relation query | 1–100 results |
| Legacy complete history/relation reads | 1,000 results; overflow throws InvalidOperationException, never partial truncation |

Oversize inputs throw `ArgumentOutOfRangeException`; null/invalid inputs throw
the existing argument exceptions. No partial writes are committed. Empty optional producer/source versions, source
kind/hash and purpose are accepted and distinct from null. Item metadata and
provenance attribute keys may be empty; snapshot metadata keys must be nonblank.
Required scope/content/kind/producer and any supplied logical/idempotency keys
must be nonblank. These preserve preview behavior rather than introducing a new
vocabulary or stricter semantics. Existing
preview data remains readable subject to bounded collection operations; existing
larger items are not reinterpreted or deleted. New oversize writes/retries fail.
The whole database and append-only retained history have no automatic size cap:
the application owns filesystem quotas, backup and retention planning. An
overflowing complete-history read does not prevent appends or latest lookup.

## Consistency, revisions and retries

Single writes, ordered batches and snapshot creation are atomic local SQLite
transactions. Immediate write transactions serialize writers; unique constraints
prevent duplicate keyed revisions and scoped retry keys. Revision exhaustion at
`Int32.MaxValue` fails with conflict rather than wrapping. Equivalent idempotent
retry is checked before expected-revision preconditions: committed success wins
over the retry's obsolete precondition. Conflicting content fails with conflict.
No transaction spans Cangjie and another store. Cancellation/errors before
commit roll back; an interrupted caller after commit must use an idempotency key
to resolve an uncertain outcome. Backend I/O/busy failures remain SQLite errors;
callers may retry transient errors, never assume an unkeyed retry is deduplicated.

Reads see SQLite statement snapshots. Separate operations need not see the same
database moment. Search orders exact matches by creation time descending then ID
ascending; lexical rank precedes those tie breakers. History orders revision
descending, creation descending, ID ascending. Relations order creation ascending,
from ID, to ID, kind ascending. Layered scopes preserve declared precedence.
Scores/lexical tokenization are provider-version specific, not cross-provider
semantic guarantees. Exact scopes/keys/metadata use ordinal identity; kind/tag
normalization follows the existing store-contract and implementation.

## Snapshot integrity and the paging profile

A persisted snapshot pins an ordered list of exact physical IDs atomically.
It does not promise search selection and pinning occur in one transaction: a
missing item between selection and persistence causes conflict with no snapshot.
Collections on items, provenance, snapshots and query inputs own read-only copies.
Caller aliases cannot change stored requests/results. Record equality of
collection properties is reference equality; use physical IDs or explicit
sequence/dictionary comparison for semantic equality. Retry equivalence uses
explicit ordered IDs and ordinal metadata, not record equality.

There is **no cursor or offset paging API** in 1.0. Repeated independent searches
are new selections; insertions/deletions/revisions can change results and they
must not be concatenated as exhaustive pages. For deterministic chunk traversal,
persist one bounded snapshot, resolve it, and traverse its in-memory ordered IDs
or items by index. Restart can resolve that same snapshot and resume at a caller
stored index. Concurrent writes, deletion attempts, expiration and revision
movement cannot change its selection or order. Each ID appears once; no omissions
are permitted. A nonexistent snapshot returns null; malformed/corrupt missing
references fail rather than returning a partial resolution. Indices are caller
state, not reusable query cursors. General paging and snapshots over 1,000 items
require a future optional capability or major version.

## Persistence and supported SQLite behavior

Schema **4** remains unchanged from preview. New databases bootstrap schema 4.
An existing schema must contain exactly one version row equal to 4. Other versions
fail closed: no migration, downgrade or destructive recreation is automatic.
1.x retains schema-4 read/write compatibility; schema changes require explicit
version policy and migration documentation. Copy/backup an idle database, or use
SQLite's online backup facility; copying an active WAL database file alone is not
a safe backup. Retain database, WAL and shared-memory files as appropriate during
recovery. Restore backup or explicitly migrate incompatible data outside this API.

Local filesystem SQLite with bundled FTS5, default WAL and foreign keys is the
supported configuration. Options are captured at construction. `EnableWal=true` requests WAL during
initialization; `false` leaves the file's existing journal mode unchanged (a new
file therefore uses SQLite's default DELETE mode). It does not switch an already
WAL-enabled database back to DELETE. `CheckHealthAsync` reports the actual mode. Multi-instance
and multi-process writers are serialized by SQLite; busy timeout may expire.
Completed transactions survive process restart/termination under SQLite's normal
local durability guarantees. No guarantee covers faulty disks, network filesystems,
external SQL tampering, distributed coordination or operating-system power failure
without platform qualification. WAL-off remains supported by SQLite transactional
semantics, with lower concurrency; qualification records identify what was tested.

`CheckHealthAsync` is read-only and does not bootstrap or migrate a missing store;
it returns unhealthy until initialization by a normal operation. It verifies
version and a trivial read, not full database integrity. Pre-cancelled probes throw
cancellation. Other failures become an unhealthy result. Constructor path setup
and explicit ordinary operations can create directories/databases.

## Retention and versioning

Ordinary deletion removes standalone records and their relations. Keyed history
and snapshot-pinned records remain protected. Expiration hides records from ordinary
search but does not delete them; host-triggered cleanup removes only unpinned
standalone expired items. No snapshot deletion, history compaction or scheduler
is promised. Application retention/export policy is outside the package.

1.x preserves public signatures, assembly identity (`1.0.0.0`), valid-input
behavior, limits, persisted meanings and ordering. Patches fix defects; minors
may add optional types/members without changing existing contracts. Breaking
source/binary/behavior/schema changes require a major version. Preview had no
compatibility guarantee. [Release notes](release-notes-1.0.md) describe intentional
preview cleanup; unchanged binary probes establish only exercised paths.

Non-goals: new engines, cursors, ranking portability, replication, consensus,
global quotas, automatic migrations, framework adoption, performance rewrites.
