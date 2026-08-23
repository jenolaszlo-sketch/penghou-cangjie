# Architecture & quality review — findings

Reviewed: 2026-08, branch `main`, 36 test cases green across 3 test projects
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

### B1. Phrase search mode joins all terms into one quoted phrase

`SafeFtsQueryBuilder.Phrase` produces `"gateway references yarp"` from
multi-word input, which only matches the exact contiguous phrase. Callers
expecting "all these words" behavior get surprising empty results. Document the
semantics prominently or map multi-word phrase input to an AND of terms.

### B2. No bulk store API

`StoreAsync` appends one revision; importing backfills or bulk context requires
N round-trips. A bulk overload (single transaction, N items) would help initial
loads.

### B3. `Kind` is a free string

`ContextKinds` supplies constants but any string is stored and later filtered
on. Consider validating against known kinds at write time (or a closed type)
once the set stabilizes — free-string kinds make cross-consumer queries fragile.

### B4. No maintenance scheduling hook for expiry

`DeleteExpiredAsync` physically deletes expired standalone items, but nothing
calls it — hosts must remember. The Zhinu hosted-service pattern or a
documented recipe would prevent silent growth. (A health probe and metrics now
exist; the sweep scheduler itself remains open.)

### B5. No benchmarks

FTS5 search latency at scale (10k/100k items), snapshot resolution cost, and
expiry sweep throughput are unmeasured. The Zhinu benchmarks pattern ports
directly.

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
2. Add **benchmarks** for FTS search and expiry sweeps at scale.
3. **Phrase-search semantics**: document prominently or remap to AND-of-terms.
4. **Relation removal/replacement** once the knowledge model needs it.
5. **Kind validation at write time** once the kind set stabilizes.
