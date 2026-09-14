# Marang Gate 0.5 — Cangjie capability audit

This note records the Cangjie baseline relevant to Marang Gate 0.5. The
baseline is package version `0.1.0-preview.3` on `main`; it is a local-first
SQLite store and has no Marang dependency. Existing Cangjie semantics remain
the source of truth; Marang-specific meaning belongs in an adapter.

## Gate-ready capabilities

The following capabilities are implemented and reusable by Marang:

- **Stable logical identity and revisions:** `ContextItem.Id` is the physical
  identity; `(Scope, Key, Revision)` identifies a logical revision. Appends are
  immutable, revision history is queryable, and supersession is linked
  atomically. See [`ContextItem`](../src/Penghou.Cangjie/ContextItem.cs) and
  [`SqliteContextStore.Items.cs`](../src/Penghou.Cangjie.Sqlite/SqliteContextStore.Items.cs).
- **Immutable ordered snapshots:** `ContextSnapshot` stores exact item IDs in
  caller-visible order, with query identity, strategy/version, selection time,
  purpose, and metadata. Creation and reference pinning are atomic; resolution
  is restart-safe and does not copy payloads. See
  [`ContextSnapshot.cs`](../src/Penghou.Cangjie/ContextSnapshot.cs) and
  [`SqliteContextStore.Snapshots.cs`](../src/Penghou.Cangjie.Sqlite/SqliteContextStore.Snapshots.cs).
- **Safe retries and competing writers:** `ExpectedRevision`, scoped
  `IdempotencyKey`, typed conflicts, and transactional ordered batches are
  implemented. See [`ContextWriteOptions.cs`](../src/Penghou.Cangjie/ContextWriteOptions.cs),
  [`IContextStore.cs`](../src/Penghou.Cangjie/IContextStore.cs), and the
  conformance suite.
- **Initial bounded retrieval and honest ranking:** `SearchAsync` supports
  exact scope/key/source/kind/tag/expiry filters, all-term/any-term/phrase
  lexical search, a limit of 1–100, deterministic ordering, and strategy/version
  metadata. Scores are strategy-local. Relation queries are also bounded to
  100. This is sufficient for an initial demand-driven adapter, but is not a
  pagination or cost-budget contract.
- **Retention guards and snapshot pinning:** `ExpiresAt` hides expired items
  from ordinary search; cleanup removes only eligible standalone items;
  keyed history and snapshot-pinned items are retained. See
  [`store-contract.md`](store-contract.md) and [`maintenance.md`](maintenance.md).
- **Portable reference-only handoff:** the existing restart proof stores a
  `cangjie://snapshot/{id}` location and a decision ID in an external workflow
  artifact, then resolves the snapshot after restart. Marang can use the same
  reference shape in `SupervisorContextPackage` without importing Cangjie
  payloads. See
  [`SoloZhinuRestartProofTests.cs`](../tests/Penghou.Cangjie.Integration.Tests/SoloZhinuRestartProofTests.cs).

## Reusable upstream follow-ups

These are not required to invalidate the initial Gate 0.5 foundation, but
should remain explicit implementation work rather than undocumented adapter
conventions:

1. **P1 — Snapshot integrity envelope:** add a canonical snapshot content hash
   over the ordered selection and a snapshot-level provenance record. The
   current snapshot has an identity and opaque `QueryIdentity`, while content
   hashes currently exist only on `ContextSource`. Marang may carry a temporary
   adapter hash in metadata, but a reusable envelope belongs upstream.
2. **P1 — Resource bounds and paging:** bound snapshot item counts, batch sizes,
   content/metadata/tag sizes, and relation enumeration; add cursor/page
   retrieval. Current search and filtered relation queries bound result counts,
   while legacy relation reads, snapshots, and batches remain unbounded.
3. **P1 — Sensitive-data policy:** define optional classification/redaction
   hooks and a storage/access policy for encryption and tenant isolation. The
   current diagnostics avoid sensitive values, but SQLite records have no
   authorization, redaction, encryption, or sensitive-data classification.
4. **P2 — Structured correlation:** add optional provider-neutral session,
   workflow, node/checkpoint, and provider correlation fields with query/index
   support. Today these can be encoded in opaque scopes/keys or provenance
   attributes, but those attributes are not indexed or queryable.
5. **P3 — True as-of retrieval:** add timestamp/revision as-of query semantics
   only if real usage requires them. Immutable snapshots already provide the
   initial reproducibility guarantee; Cangjie deliberately defers workflow
   replay and richer semantic replay.

## Marang adapter ownership

The Marang adapter should own:

- stable mapping of session/workflow/node/checkpoint IDs into Cangjie scopes,
  keys, provenance attributes, and/or relations;
- pre-redaction, authorization, tenant/access policy, and caller-side byte or
  token budgets until the upstream contracts exist;
- creation of a snapshot immediately after selection, with an adapter-level
  canonical hash if needed;
- reference-only `SupervisorContextPackage` entries containing snapshot/item
  references and optional integrity metadata, never copied context payloads.

Cangjie remains responsible for durable immutable records, revision history,
ordered snapshots, retrieval results, and reference resolution. It does not
execute workflows, interpret sessions, or depend on Marang.
