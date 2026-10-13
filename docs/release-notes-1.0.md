# 1.0 graduation release notes and migration

Target: `1.0.0-rc.1` qualification followed by `1.0.0`, never stable before gates.
All three packages retain their .NET 10 public signatures and SQLite schema 4.
Shipped API baselines intentionally include the complete existing core, provider,
DI/diagnostic and framework-neutral conformance surface.

Preview-to-stable changes: bounded input/collection reads; defensive read-only
collection ownership; options captured at construction; explicit revision
exhaustion/corrupt snapshot failure; read-only health no longer initializes a
missing database. Initialize via a normal store operation before readiness probes.
Replace mutation of model collections with construction/`with` initialization.
Reduce oversize requests/batches/selections; split independent workloads. Do not
split a single required atomic batch into several transactions accidentally.
History over 1,000 revisions is retained and latest lookup works, but complete
materialization fails. Plan application export/retention separately. There is no
cursor migration because preview had no cursor API.

Back up existing schema-4 databases before rollout. No data rewrite or migration
is needed. Other schema versions require a separately planned explicit migration,
not deletion/recreation of valuable application context.

Exact-package qualification uses `scripts/qualify-packages.ps1`: isolated package
restore, conformance, child-process termination with an uncommitted SQLite
transaction, committed pinned snapshot reopen, unchanged compiled preview consumer
with only package DLL replacement (baseline is executed before replacement),
retained baseline-created schema-4 snapshot reopened by the candidate, and unchanged
Solo/Zhinu cross-store restart
test. JSON evidence records source/version/OS/date/binary SHA. The release workflow
qualifies exact artifacts on Linux and Windows and publishes those same Ubuntu
artifacts; SHA256 manifests/artifacts remain attached to the workflow run. Local qualification maps all Cangjie packages exclusively to the artifact feed
and asserts project-free exact versions and ZIP-entry SHA256 payloads. Public
restore repeats qualification using a new packages directory and compares every
ZIP entry to the reviewed package, excluding only the NuGet signature entry.
Runtime probes assert loaded Cangjie assembly hashes. No claim extends
to unexecuted consumers, macOS, power-loss hardware or unrelated Zhinu semantics.

Stable gate: both platform tests and exact artifacts pass, public RC restores and
passes the probe, shipped API policy/docs complete, representative consumer path
passes, and no inventory must-resolve item remains. Stable packages repeat public
qualification and are tagged at the source release commit.
