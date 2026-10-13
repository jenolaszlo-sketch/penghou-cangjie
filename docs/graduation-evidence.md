# Cangjie graduation qualification evidence

Repository: https://github.com/jenolaszlo-sketch/penghou-cangjie.
Release lineage: `main`; isolated branch `codex/graduation-cangjie`.
Qualification date: October 14, 2026 (Asia/Manila). Target framework: `net10.0`.

## Qualified RC

Version `1.0.0-rc.1`, tag `v1.0.0-rc.1`, exact source
`fd7ce9dcc83a4de47f929eef23fd421fcf802582`.
[Release run 38438595219](https://github.com/jenolaszlo-sketch/penghou-cangjie/actions/runs/38438595219)
retains release packages, source commit, SHA256SUMS, exact Windows/Linux and public
qualification JSON. Early public attempts failed only on NuGet indexing; the final
public gate passed against the same artifacts. NuGet signing changes the outer
ZIP hash; every entry name/payload hash except `.signature.p7s` was identical.

Original unsigned RC nupkg SHA256:

| Package | SHA256 |
|---|---|
| Penghou.Cangjie | 34a4c3d3edbdfadf7a9278e1c91eea193d63974661052c765ddc226e1b1621c0 |
| Penghou.Cangjie.Sqlite | 674efc5bd47f7e5b981989eeda5a6554b97d11a94c3025d8a68afab2dc164436 |
| Penghou.Cangjie.Testing | f383aa6bba1c56db0861cd6b2edd0403b22f8e21af60ce5e8f2c366bd60b2404 |

`SUPPORTED BY THIS PROBE`: 47 source tests (6 core,40 SQLite,1 cross-store),
format/build/pack on Windows/Linux; exclusive artifact feed mapping; project-free
exact package graph; ZIP payload equality; loaded assembly hash checks; reusable
conformance; three concurrent writer processes with 30 contiguous unique revisions;
committed snapshot reopen after process kill; rollback of an uncommitted SQLite
snapshot transaction; retained preview-created schema-4 snapshot reopened by the
candidate; unchanged preview-compiled consumer; six isolated packaged graduation
and cross-store tests. Process-kill proof covers SQLite transaction recovery, not
arbitrary machine power-loss hardware.

Independent Windows public proof: Windows10.0.26200, actual .NET10.0.12,
SDK10.0.401, source ref `codex/graduation-cangjie`, RC source above. Exact CI
Windows used Windows10.0.26100; Linux used Ubuntu24.04.5LTS. Legacy RC CI JSON's
`runtime` field was the SDK version (10.0.401/10.0.402); stable JSON records SDK and
actual runtime separately, with repository, source ref, source SHA and target.

Representative Guyabano at `df845087a663b51929c081f7e83b9852598bc995`: eight
unchanged tests passed through a clean public RC cache, changing only Cangjie
package references in an isolated checkout. Five revision/concept tests cover
idempotent saga receipts/events, regenerated decision history, evidence attribution,
derived-from relations and scope isolation. Three artifact-indexing tests cover
provenance, idempotency and revision supersession. This proves those exercised paths,
not the entire application. Original dirty checkouts were preserved.

The unchanged Solo/Zhinu restart test uses public Zhinu/Sqlite `0.1.0-preview.14`.
An additional package-only execution of the same test passed with current public
Zhinu/Sqlite `0.2.0-preview.3`; no implementation redesign was required.

## Stable gate and evidence location

Stable target `1.0.0` preserves RC product implementation/APIs. SDK package
validation and strict RC1 baseline validation are explicitly enabled; evaluated
property checks prevent an early `IsPackable` condition from disabling the gate.
Stable binary qualification compiles against public `1.0.0-rc.1`, executes that
baseline, and substitutes only the three stable DLLs without recompilation.

Manual trusted publication from the clean intended main commit must pass exact
Windows/Linux probes and public ZIP payload/behavior proof before provenance-only
tag `v1.0.0` identifies that same source. The stable run on the
[publish workflow](https://github.com/jenolaszlo-sketch/penghou-cangjie/actions/workflows/publish.yml)
retains exact nupkg/snupkg hashes, source and JSON evidence. Post-publication checks
repeat the eight representative Guyabano tests. RC success alone does not prove a
stable artifact; final stable run/tag and public validation belong in the handoff.

`NOT PROVEN`: macOS, unexercised providers/consumers, hardware power loss,
faulty disks/network filesystems, external SQL tampering.
`OUT OF SCOPE`: cursor paging, new engines, automatic migrations, retention policy,
replication, distributed atomicity and unrelated preview runtimes.

## Final stable result

`SUPPORTED BY THIS PROBE`: all three `1.0.0` packages are published and restored
publicly. [Stable trusted run38484745880](https://github.com/jenolaszlo-sketch/penghou-cangjie/actions/runs/38484745880)
and main CI38484743419 passed. Exact source and provenance-only `v1.0.0` tag:
`3af06c58e2b283d8286e53e85c663144a39bab51`.

[Retained qualification files](qualification/1.0.0/) contain original artifact
SHA256SUMS, exact/public Linux and Windows evidence, public-RC1 binary baseline,
retained schema-4 snapshot proof and eight final public-package Guyabano tests.
Public ZIP payloads matched the original qualified stable artifacts, excluding
only NuGet's signature entry. Linux used SDK10.0.402 / .NET10.0.13; independent
Windows used SDK10.0.401 / .NET10.0.12. Release source remains separate from this
later evidence-only commit. No product source or API changed after qualification.

Remaining graduation blockers: none. Deliberate exclusions above remain future
work and do not require a new feature cycle. Stable tags cannot trigger another
publication. Stop graduation work on this component.
