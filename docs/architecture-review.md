# Architecture & quality review — findings

Reviewed: 2026-08, branch `main`, 39 test cases green across 3 test projects
(including a Solo+Zhinu cross-repo restart proof). Read-only audit; no code
changes accompany this document.

Scope: all 3 packages — core abstractions (`Penghou.Cangjie`), SQLite/FTS5
implementation (`Penghou.Cangjie.Sqlite`), conformance harness
(`Penghou.Cangjie.Testing`) — plus samples and integration tests.

## Summary

For its size this project is in good shape: immutable-revision durability model,
schema versioning from day one (v4 verified at init), an injection-safe FTS5
query builder, a conformance suite that tests actually invoke, correct PublicAPI
baseline casing, and an existing Solo+Zhinu cross-repo restart proof.

**Resolved since review:** the `SqliteContextStore` god class was refactored into
a facade + database owner + capability partials; schema v4 added CHECK
constraints; metrics and a health probe were added; connection pooling became
configurable; CI gained a format gate and the repo gained an `.editorconfig`.
Phrase semantics are now explicit in API and user documentation, host-triggered
maintenance has a documented recipe, and a BenchmarkDotNet harness covers the
initial performance-sensitive paths.

## A. Architecture

### A1. Wide `IContextStore` contract

Twelve methods spanning items, key history, search, snapshots, relations, and
expiry in a single interface. Acceptable today, but relations and snapshots are
independent concepts that will grow (deletion policies, relation queries with
pagination); consider keeping `IContextStore` focused on items/search and
exposing relations/snapshots as separate capabilities or extension surfaces.

### A2. Relations are add-only

`AddRelationAsync` is idempotent-add and item deletion cascades relations, but
there is no way to remove or replace a relation between two live items. If
relations model evolving knowledge (superseded-by, corrected-by), a remove or
replace operation will eventually be required.

## B. Usability & usefulness

### B1. Phrase search mode joins all terms into one quoted phrase — documented

`SafeFtsQueryBuilder.Phrase` produces `"gateway references yarp"` from
multi-word input, which only matches the exact contiguous phrase. Callers
expecting "all these words" behavior could get surprising empty results. The
README, XML API documentation, and regression tests now state that phrase mode
is exact and contiguous and direct callers to `AllTerms` otherwise.

### B2. No bulk store API

`StoreAsync` appends one revision; importing backfills or bulk context requires
N round-trips. A bulk overload (single transaction, N items) would help initial
loads.

### B3. `Kind` is a free string

`ContextKinds` supplies constants but any string is stored and later filtered
on. Consider validating against known kinds at write time (or a closed type)
once the set stabilizes — free-string kinds make cross-consumer queries fragile.

### B4. Host-triggered expiry maintenance — documented

`DeleteExpiredAsync` physically deletes expired standalone items. Scheduling is
intentionally owned by the host or an external system; `docs/maintenance.md`
now provides hosted-service and one-shot job guidance.

### B5. Benchmark harness added; baselines pending

The BenchmarkDotNet project covers FTS5 and layered-scope search at 10k/100k
items, snapshot resolution, expiry sweeps, and sequential/concurrent ingestion.
Stable machine baselines should be recorded after a representative CI or
developer environment is selected.

## Done well (preserve)

1. Immutable revisions with logical keys, monotonic revision numbers, and
   protected keyed history — a clean temporal model.
2. Idempotency keys with request-hash comparison producing either the original
   item or a typed conflict — exactly right for retries.
3. `SafeFtsQueryBuilder`: regex-normalized distinct terms and proper FTS5 quote
   escaping — injection-safe lexical search.
4. Schema versioning checked at initialization from day one, now with CHECK
   constraints on scope/kind/content/revision and self-relation rejection.
5. Snapshot pinning with recorded-order resolution — reproducible consumer
   inputs, which is the project's core promise.
6. The Solo+Zhinu integration restart proof already exists and exercises both
   runtimes over real files.
7. PublicApi baselines enabled and correctly cased.
8. Metrics (`cangjie.items.stored`, `cangjie.expired.deleted`,
   `cangjie.search.duration`) and a `CheckHealthAsync` probe are now wired.

## Suggested priority

1. **Refactor follow-through**: when relations/snapshots grow, split their
   public surface off `IContextStore` rather than widening it further.
2. Record and compare **benchmark baselines** on a representative machine.
3. **Phrase-search semantics**: preserve the now-documented exact behavior.
4. **Relation removal/replacement** once the knowledge model needs it.
5. **Kind validation at write time** once the kind set stabilizes.

## Follow-up decisions

- Expiration scheduling belongs to the host or an external scheduler. Cangjie
  keeps `DeleteExpiredAsync` as the explicit trigger and will document safe
  hosting patterns rather than run a mandatory background loop.
- Phrase mode remains exact and contiguous. The API and README should state
  that clearly; `AllTerms` is the mode for words that may appear separately.
- Benchmarks should precede retrieval or maintenance optimization.
- The wide `IContextStore` surface remains acceptable until another store or a
  real consumer demonstrates a need for capability-specific interfaces.
- Bulk writes and relation correction/removal need real ingestion and knowledge
  lifecycle use cases before their atomic and audit semantics are designed.
- Extensible string kinds are intentional. Do not close the vocabulary merely
  for validation convenience; revisit only if interoperability evidence shows
  that namespacing and constants are insufficient.
- The next product-level validation is real Solo dogfooding. The current
  integration test proves persistence boundaries but does not replace normal
  authoring experience in Solo.
