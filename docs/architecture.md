# Cangjie architecture

## Role

Penghou.Cangjie is a domain-neutral, local-first store for explicit context. It
persists immutable revisions, provenance, relations, and retrieval indexes. It
does not interpret conversations, execute workflows, call models, or decide
what should enter a prompt.

```text
Application / Solo / workflow activity
        ↓ explicit store and query contracts
Penghou.Cangjie
        ↓ implementation-neutral IContextStore
Penghou.Cangjie.Sqlite
        ↓
SQLite records + relations + tags + FTS5
```

## Packages

| Package | Responsibility |
| --- | --- |
| `Penghou.Cangjie` | Immutable context, provenance, relation, query, and store contracts. |
| `Penghou.Cangjie.Sqlite` | Transactional SQLite persistence and lexical FTS5 retrieval. |
| `Penghou.Cangjie.Testing` | Reusable `IContextStore` fixture and conformance suite for implementations. |

The core package has an executable dependency-boundary test preventing Baize,
Zhinu, OpenAI, or Anthropic references.

## Context model

`ContextItem.Id` identifies one immutable physical record. A keyed logical
concept is identified by `(Scope, Key)` and contains monotonically increasing
`Revision` records. An unkeyed item is a standalone revision 1 record.

`Kind` and relation kinds are extensible stable strings. The core exposes
well-known constants but does not own an exhaustive vocabulary.

Every item has `ContextProvenance`:

- `Producer` identifies the component or logical process that recorded it;
- optional `ProducerVersion` identifies that implementation or model version;
- optional `Source` identifies external origin, classification, and content
  hash;
- optional `OriginatedAt` distinguishes source time from store time;
- string attributes carry provider-neutral extension metadata.

`CreatedAt` is assigned by the store when omitted and records when that
physical revision entered Cangjie.

## Write path

`StoreAsync` performs one atomic immutable append:

1. validate and normalize the request;
2. acquire the implementation's write transaction;
3. resolve an existing scoped idempotency key, if supplied;
4. verify physical ID uniqueness;
5. read the current keyed revision;
6. verify `ExpectedRevision`, if supplied;
7. assign revision 1 or the next logical revision;
8. insert the record, tags, and retrieval index;
9. link a new keyed revision to its predecessor with `supersedes`;
10. commit atomically.

Equivalent idempotent retries return the original physical record. Reusing the
same scoped idempotency key for different semantic content fails with
`ContextStoreConflictException`.

## SQLite persistence

The SQLite implementation owns its database file and opens short-lived pooled
connections per operation. Initialization is lazy and guarded within each store
instance. Foreign keys are enabled for every connection; WAL and busy timeout
are configurable.

Schema version 3 contains:

- `context_items` for immutable records, revision identity, provenance,
  metadata, expiration, idempotency keys, and canonical request hashes;
- `context_tags` for normalized exact tag filters;
- `context_relations` for directed, extensible relationships;
- `context_snapshots` and `context_snapshot_items` for immutable ordered
  selections and reference pinning;
- `context_items_fts` for FTS5 lexical search.

Unique indexes enforce `(scope, logical_key, revision)` and scoped idempotency.
SQLite immediate write transactions serialize revision allocation. Database
constraint races are surfaced as context-store conflicts rather than leaking
provider-specific exceptions.

There are no migrations while the package has no persisted user data. An older
preview schema is rejected with an instruction to recreate the database.

## Retrieval

Current retrieval is exact-filtered lexical search:

- exact scope, logical key, source URI, and kind filters;
- caller-ordered exact scope sets, with precedence before lexical relevance;
- deterministic logical-key deduplication across requested scopes;
- all-requested-tags semantics;
- normalized safe FTS5 all-term, any-term, or phrase queries;
- optional inclusion of expired items;
- bounded results;
- deterministic tie-breaking.

`ContextSearchHit.Rank` is the one-based position in one result set. It is not a
globally comparable relevance or confidence score.

Each hit carries a stable strategy identifier and an implementation-defined
strategy version. `Score` is optional and strategy-local; the SQLite initial
vertical deliberately leaves it unset until its direction and useful range can
be exposed without suggesting cross-query comparability.

For layered retrieval, scope identifiers remain opaque. The caller supplies
their precedence explicitly through `ContextQuery.Scopes`. A keyed concept is
selected from its first matching scope; unkeyed records are never conflated.
Cangjie does not parse scope delimiters or infer application hierarchy.

Relationship retrieval remains one hop and implementation-neutral. The legacy
`GetRelationsAsync` operation preserves its existing unbounded behavior;
`QueryRelationsAsync` adds explicit direction, exact kind filters, a maximum of
100 results, and deterministic ordering. `GetRelatedItemsAsync` resolves the
opposite item and reports dangling relations as integrity failures. Multi-hop
graph traversal remains outside the current contract.

## Diagnostics

Cangjie uses the built-in .NET `ActivitySource` named `Penghou.Cangjie`.
SQLite search emits `context.search` with structural tags for strategy,
requested limit, filter counts, expiration mode, and result count. It does not
emit query text, stored content, scope/key/source values, or tag values. When no
listener is installed, no activity is allocated.

## Lifetime and deletion

Ordinary `DeleteAsync` physically removes standalone items and their relations.
Keyed revisions are protected because removing one would destroy semantic
history and could permit revision reuse. Expiration hides all expired items from
ordinary search, while cleanup physically removes only standalone expired
records.

Immutable snapshots store ordered physical item references and selection
metadata without duplicating payloads. SQLite foreign keys and one immediate
transaction make snapshot creation atomic. Snapshot references pin standalone
and keyed revisions against ordinary deletion and expiration cleanup. Explicit
administrative purge remains later roadmap work.

## Verification

Public API Analyzer baselines protect all packable packages. The SQLite test
suite covers store-specific behavior and runs the reusable
`ContextStoreConformanceSuite` through a fresh peer store over the same durable
database.
