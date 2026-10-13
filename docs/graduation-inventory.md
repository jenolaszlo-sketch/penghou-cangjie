# Cangjie graduation inventory (phase 0)

- Repository: `jenolaszlo-sketch/penghou-cangjie`; source `main` at `d719f8ca751ae3c2d6543c7f8ac77c2a3f45e786`; isolated branch `codex/graduation-cangjie`. Original modified ROADMAP is excluded.
- Published baseline: `0.1.0-preview.3`; packages core, Sqlite, Testing; all target .NET 10.
- Public API: all declarations are Unshipped, Shipped baselines empty. No intentionally experimental public members found. Minimal existing `IContextStore` implementations genuinely support every member; preserve signatures and use separate optional capability interfaces for future capabilities.
- Dependencies: core and Testing have no runtime external packages; Testing references core. Sqlite references core, Microsoft.Data.Sqlite 10.0.10, DependencyInjection.Abstractions 10.0.9, SQLitePCLRaw.bundle_e_sqlite3 2.1.12.
- Consumers: sample, SQLite conformance fixture; Solo/Zhinu cross-store restart proof uses public Zhinu/Sqlite 0.1.0-preview.14. Guyabano consumes core/Sqlite. The full program assessment records Marang snapshot context.
- Persisted format: SQLite schema version 4, immutable items, ordered snapshot references, request hashes and FTS5 index. No wire protocol or automatic migrations.
- Existing evidence: shared conformance; atomic batch rollback; concurrent unconditional and expected-revision writes; idempotent peers; snapshots preserve exact IDs/order across new store instances; missing-reference atomic rollback; pinning/cleanup; workflow artifact references reconstructed after engine restart.
- CI: Linux .NET 10 restore/build/format/test/pack. Publication: tagged `v*` or manual workflow, NuGet trusted publishing via OIDC; three package and symbol artifacts.

## Disposition before changes

| Item | Classification | Narrow decision |
|---|---|---|
| Snapshot/batch/content/metadata and legacy collection reads lack maxima | Must resolve | Fixed documented limits; reject writes before transaction; legacy reads fail on overflow without silent truncation. |
| Snapshot collections retain mutable caller aliases | Must resolve | Defensive read-only copies in model initialization; immutable pinned selection. |
| Paging semantics | Must resolve | No cursor API exists. Stable profile is bounded query plus in-memory traversal of one persisted snapshot; separate queries are independent, not pages. No new cursor capability. |
| Schema policy | Must resolve | Version 4 only; incompatible versions fail; no automatic migration/downgrade; backup before future explicit migration. |
| Crash termination evidence | Must resolve | Qualify a child process terminated with open transaction, and completed snapshot across process restart. |
| Binary/package qualification | Must resolve | Isolated packed consumer and unchanged compiled consumer with artifact substitution; public RC then stable validation. |
| Ordering/equality | Must resolve | Existing SQL ordering, exact identity, semantic retry comparison; record collection equality is reference equality and not semantic content equality. |
| Global database size/retention scheduler | Explicitly unspecified | Host supplies storage quota/backup/scheduling; keyed histories and snapshots are retained, not automatically pruned. |
| Future engines, search ranking portability, cursor paging | Future work | Optional interfaces or next major; no new abstract members in 1.x. |
| Replication/distributed atomicity/remote filesystems | Out of scope | Local SQLite transaction boundaries only. |
