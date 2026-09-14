# Penghou.Cangjie Roadmap

## Objective

Prepare Penghou.Cangjie to become a durable working-context and shared-cognition
substrate for long-running Penghou workflows without coupling the core library
to Guyabano/Solo, coding agents, Zhinu, Baize, or any model provider.

```text
Solo
├── session and conversational semantics
├── coding-domain semantics
├── context resolution and compilation
│
├── Cangjie: context, provenance, revisions, relationships, retrieval
├── Zhinu: execution, attempts, dependencies, recovery, replay
└── Baize: provider and model execution
```

Cangjie answers:

> What has been retained, who asserted or observed it, where did it come from,
> how has it changed, how is it related, and what may be relevant now?

Zhinu answers what executed and what should execute next. Solo interprets the
knowledge and conversation in the software-engineering domain. Baize
communicates with the selected model.

## Architectural boundaries

- Cangjie remains domain-neutral. Coding decisions, repository observations,
  Solo sessions, and workflow memory are application or integration concepts.
- Persistence is provider-neutral. Canonical records do not contain Baize or
  provider-native messages.
- Durable knowledge is not synonymous with transcript history. The caller
  decides what deserves durable representation.
- Cangjie stores semantic context; it does not execute workflows, invalidate
  dependencies, or implement replay.
- Zhinu and Solo should retain stable Cangjie references instead of copying
  complete payloads into workflow state.
- Persistence remains provider-neutral. SQLite is the proven initial authority;
  LatticeDbSharp is the target first graph-capable provider, but replaces
  SQLite only after restart, replay, corruption, query-equivalence, and package
  isolation gates pass. Distributed and cloud architectures remain out of
  scope until demand exists.

## Core semantic laws

### Immutable physical revisions

A stored physical item is immutable. Updating a logical concept creates a new
revision rather than modifying the earlier item:

```text
(scope, logical key, revision 1) ── superseded-by ──► revision 2
```

- `Id` is an opaque physical revision identity.
- `(Scope, Key, Revision)` identifies one logical revision.
- Revision numbers increase monotonically within an exact scope and key.
- An item without a logical key is a standalone immutable record with revision
  1 and does not participate in a revision chain.
- Appending may accept an expected current revision for optimistic concurrency.
- A successful append records or derives the supersession relationship
  atomically.
- Reusing an existing physical ID with different semantic content is rejected.
- Administrative mutation, if ever needed, must be narrowly separated from
  semantic content.

### Identity and idempotency are separate

Physical IDs are stable after assignment but need not be content-derived.
Callers may supply a separate idempotency key when the same ingestion operation
can be retried. An idempotency key is unique within an exact context scope;
callers may namespace its value by producer or operation. The store returns the
original result for an equivalent retry while rejecting conflicting reuse.

### Extensible classifications

Context and relationship kinds are stable text identifiers. Cangjie provides
well-known constants without closing the vocabulary:

```csharp
ContextKinds.Evidence
ContextKinds.Knowledge
ContextKinds.Decision
ContextRelationKinds.DerivedFrom
ContextRelationKinds.Supersedes
```

Applications and integration packages may define their own namespaced kinds.

### Honest retrieval

- Exact filters and ordering are deterministic.
- Ranking scores are strategy-specific and are not presented as globally
  comparable confidence.
- Retrieval results identify the strategy and version that produced them.
- Cangjie returns candidates; Solo owns interpretation, ambiguity resolution,
  and user clarification.

### Snapshot retention

An immutable context snapshot pins every referenced physical revision. Normal
deletion and expiration cleanup cannot remove pinned revisions. A future
administrative purge must be explicit and must report affected snapshots; it
must never silently make a reproducible snapshot incomplete.

## Current baseline

The existing implementation already provides:

- provider-neutral context records;
- SQLite persistence and safe FTS5 lexical retrieval;
- exact scopes, logical keys, tags, metadata, and expiration;
- physical item IDs and logical history queries;
- extensible directed relationships with forward and reverse lookup;
- source URI, source kind, and source content hash;
- deterministic result ordering and bounded result counts;
- transactional writes and basic concurrent-write coverage.

The roadmap therefore hardens and extends this working vertical rather than
rebuilding it.

## Phase C1 — Audit and semantic hardening

Status: complete for the initial SQLite vertical. Immutable revisions,
optimistic concurrency, scoped idempotency, extensible kinds, retention guards,
architecture and semantic-contract documentation, Public API baselines, and a
reusable store conformance suite are implemented.

Document the current public API, persistence schema, serialization, ownership,
concurrency behavior, and retrieval semantics. Resolve foundational contracts
before expanding the feature set.

Deliverables:

- architecture and semantic-contract documents;
- immutable append/revise behavior with explicit revision numbers;
- optimistic concurrency for competing revisions;
- caller-supplied idempotency keys with conflict detection;
- extensible string context kinds and well-known constants;
- explicit distinction between semantic retention and physical purge;
- Public API Analyzer shipped/unshipped baselines;
- a reusable `IContextStore` conformance suite;
- tests for concurrent appends to the same logical key.

Acceptance criteria:

- an existing physical revision cannot be semantically overwritten;
- two competing writers cannot silently create the same logical revision;
- an equivalent idempotent retry returns the original stored revision;
- conflicting reuse of an idempotency key fails explicitly;
- the current build, tests, formatting, and package validation remain green.

## Phase C2 — First-class provenance

Status: initial vertical implemented. Source and producer are distinct,
provider-neutral provenance fields; restart and conformance hardening remain.

Introduce a provider-neutral provenance record that distinguishes the origin of
information from the process that recorded or derived it.

Conceptually:

```text
ContextProvenance
├── source URI
├── source kind
├── source revision or content hash
├── producer identifier
├── producer version (optional)
├── originated at (optional)
└── extensible attributes
```

The producer might be an application component, tool invocation, workflow
attempt, person, or model-backed activity, but the core does not define
Solo-specific enums.

Acceptance criteria:

- every returned item can identify when it was recorded;
- items can identify where information originated and what produced the record;
- provenance survives restart and JSON/SQLite round trips;
- sensitive or provider-native payloads are not required;
- superseded revisions retain their original provenance.

## Phase C3 — Durable provider-neutral restart proof

Status: initial SQLite proof implemented with fresh producer and consumer store
instances; expand through the future store conformance suite.

Prove the smallest complete vertical:

```text
logical producer A stores an observation
        ↓
process/store closes
        ↓
fresh store instance opens
        ↓
logical consumer B retrieves and projects it
```

The fixtures represent different model consumers without invoking external
models.

Acceptance criteria:

- physical references remain stable across restart;
- immutable content and provenance round-trip exactly;
- the representation has no Baize or provider dependency;
- relations and logical revision history survive restart;
- tests use a genuinely fresh store instance and connection lifecycle.

## Phase C4 — Relationships and revision history

Status: initial vertical complete. The store now supports filtered and bounded
one-hop traversal, related-item projection, atomic supersession, keyed history
helpers, deterministic ordering, and history protection. Multi-hop traversal
remains intentionally deferred pending a concrete retrieval use case.

Harden the existing graph around exact physical revisions.

At minimum retain extensible well-known relationships for:

```text
relates-to
supports
contradicts
supersedes
derived-from
references
```

Deliverables:

- relation-kind filtering;
- forward and reverse neighbor retrieval;
- atomic revision append plus supersession linkage;
- helpers for latest revision and complete logical history;
- deterministic traversal limits and ordering;
- protection against accidental loss of revision history.

This is not an arbitrary graph engine. Multi-hop graph queries should be added
only when a demonstrated retrieval use case requires them.

## Phase C5 — Scoped retrieval

Status: initial SQLite vertical implemented with caller-ordered exact scopes,
scope-first deterministic ranking, logical-key deduplication, isolation tests,
and reusable provider conformance coverage.

Scopes remain opaque, exact, caller-owned namespaces. Cangjie does not infer
hierarchy from delimiters or hard-code Solo concepts such as repository,
session, workflow, or step.

For layered retrieval, the caller supplies an ordered scope set:

```text
current step
current workflow
current repository
broader project
```

Deliverables:

- exact-scope retrieval;
- ordered multi-scope queries with explicit precedence;
- isolation between unrelated scopes;
- deterministic deduplication when a logical concept appears in several
  requested scopes;
- bounded result counts and stable tie-breaking.

Acceptance criteria:

- no unrelated scope leaks into a result;
- caller-declared scope precedence is observable and reproducible;
- the core makes no assumptions about the meaning of scope identifiers.

## Phase C6 — Retrieval contract and diagnostics

Status: initial strategy metadata and privacy-safe diagnostic activity vertical
implemented. Strategy-local score semantics and richer non-sensitive match
details remain deliberately deferred until their contracts are well-defined.

Stabilize a narrow contract that can support exact, lexical,
relationship-aware, semantic, and hybrid strategies without coupling the core
to an embedding provider.

Conceptually:

```csharp
ContextQuery
{
    Text
    Scopes
    Limit
    Filters
    RelationshipConstraints
}

ContextSearchHit
{
    Item
    Rank
    Score?          // strategy-local only
    Strategy
    StrategyVersion
    MatchDetails?
}
```

The initial implementation remains lexical and minimal. Embeddings and vector
infrastructure remain optional extensions.

Acceptance criteria:

- lexical ranking and tie-breaking are deterministic;
- results say which strategy produced them;
- any exposed score has documented direction, range if applicable, and
  comparison limits;
- query diagnostics can explain filters and matched terms without logging full
  sensitive content by default;
- retrieval behavior is covered by the store conformance suite.

## Phase C7 — Immutable context snapshots

Status: initial SQLite vertical implemented with atomic immutable creation,
ordered restart-safe reconstruction, exact physical references, selection
metadata, and deletion/expiration pinning.

Snapshots record exactly what a consumer received and why it was selected.

```text
ContextSnapshot
├── snapshot ID
├── exact item revision references
├── query/specification identity
├── retrieval strategy and version
├── selection timestamp
├── caller-provided purpose/reason (optional)
└── selection metadata (optional)
```

Snapshots contain stable references rather than duplicated context payloads.
Creating a snapshot atomically pins its referenced revisions.

Acceptance criteria:

- snapshots are immutable and restart-safe;
- every reference resolves to the exact historical revision;
- knowledge evolution does not alter existing snapshots;
- ordinary deletion and expiration cleanup preserve pinned revisions;
- reconstruction reports ordering and selection metadata deterministically;
- concurrent snapshot creation and cleanup cannot produce partial snapshots.

## Phase C8 — Minimal Solo and Zhinu integration proof

Status: initial black-box proof implemented against Zhinu's published durable
artifact contract. Separate logical research and architecture providers write
provenance, restart through fresh Cangjie and Zhinu instances, reconstruct an
exact snapshot, and retain only snapshot/decision references in Zhinu.

Do not make core Cangjie depend on Solo or Zhinu. Solo may initially consume
both libraries directly. Create an integration package only if it contains a
clear reusable contract rather than package symmetry.

Prove:

```text
Solo research activity
    stores observations and provenance in Cangjie
        ↓ restart
Solo architecture activity
    retrieves through another logical provider
    records the exact context snapshot
    stores a decision derived from observations
        ↓
Zhinu attempt state retains snapshot and produced-context references
```

Acceptance criteria:

- the research and architecture consumers can be backed by different models;
- provider-native conversation history is unnecessary;
- the architecture decision retains derivation and producer provenance;
- Zhinu stores references, not copied context payloads;
- a later retry may intentionally use a new snapshot without changing the old
  attempt's evidence;
- the complete proof survives process restart.

Stop after this phase and dogfood the design before adding richer memory
behavior.

## Phase C9 — Dogfooding write ergonomics

Status: complete. Guyabano's repository-context integration exposed two
provider-neutral persistence gaps, now covered by the core contract, SQLite
implementation, and reusable store conformance suite.

- Ordered `StoreBatchAsync` writes are transactional: either every new item is
  committed or none is.
- Per-item optimistic concurrency and scoped idempotency retain their normal
  semantics inside a batch.
- Caller-identified snapshots support equivalent retries without a
  read-before-create race.
- Reusing a snapshot ID for a different selection remains an explicit conflict.

Rendering, prompt disclosure, repository identity, and selection policy remain
application responsibilities rather than Cangjie concepts.

## Marang Gate 0.5 — Capability baseline

The initial Gate 0.5 foundation is usable: stable logical revisions,
immutable ordered snapshots, `ExpectedRevision` and idempotent retries,
bounded lexical search with strategy-aware ranking, retention/pinning guards,
and portable snapshot IDs are implemented. Marang can place snapshot
references in `SupervisorContextPackage` without copying Cangjie payloads.

The detailed evidence matrix, adapter boundary, and prioritized upstream work
are recorded in [`docs/marang-gate-0.5.md`](docs/marang-gate-0.5.md). The main
follow-ups are a snapshot-level canonical hash/provenance envelope, resource
size bounds and paged retrieval, optional structured correlation/indexing, and
sensitive-data classification/redaction/encryption/tenant policy. True as-of
retrieval remains later work because immutable snapshots already provide the
initial reproducibility guarantee.

## Later, evidence-driven work

### Phase C10 — Attributed working-memory semantics

Evolve Cangjie from generic retained context into an explicitly attributed
shared cognitive workspace without treating model-authored content as fact.

- [ ] Define bounded diary-entry profiles for observations, assumptions,
  hypotheses, concerns, constraints, questions, decisions, and suggestions.
- [ ] Bind each entry to opaque run/workflow/activity, actor, model/version,
  role/profile, scope, occurrence time, producer, and evidence references where
  supplied. Keep application identity vocabularies outside core.
- [ ] Make epistemic status and source strength explicit and extensible; avoid
  a universal confidence score.
- [ ] Add first-class `supports`, `contradicts`, `refines`, `invalidates`,
  `answers`, `depends-on`, `derived-from`, and `supersedes` relationships while
  retaining immutable physical revisions.
- [ ] Specify that memory payloads are untrusted context, never workflow
  instructions, capability grants, approvals, or control-plane authority.

### Phase C11 — LatticeDbSharp graph-provider proof

Graph relationships are part of Cangjie's portable memory semantics; native
LatticeDB identities and query syntax are not. Introduce an optional provider
and prove it before changing the default store.

- [ ] Define a versioned mapping for entries, revisions, relationships,
  snapshots, provenance, scopes, retention state, and provider checkpoints.
- [ ] Implement exact/scoped lookup, relationship traversal, lexical search,
  and optional vector/hybrid capabilities with typed unsupported results.
- [ ] Preserve stable Cangjie IDs and canonical payloads independently of
  native node/edge IDs.
- [ ] Prove idempotent replay, optimistic concurrency, multi-writer behavior,
  process restart, delete/rebuild, corruption detection, and deterministic
  result bounds on every supported native platform.
- [ ] Compare SQLite and LatticeDB results through the store conformance suite.
  Retire SQLite as the primary architecture only after parity and operational
  recovery are demonstrated; no distributed dual-write is introduced.

### Phase C12 — Purpose-specific context compilation

- [ ] Add bounded activity, checkpoint, planning, and review context requests;
  scopes and external identities remain opaque caller inputs.
- [ ] Make checkpoint context delta-oriented from an explicit prior snapshot or
  cursor instead of repeatedly returning full workflow memory.
- [ ] Return an immutable context snapshot plus selection policy/version,
  truncation, diagnostics, and evidence/provenance references.
- [ ] Track retrieval, inclusion, expansion, citation, decision use,
  contradiction, validation, and replan outcomes as external evidence signals;
  do not let Cangjie infer causal usefulness from retrieval alone.

### Phase C13 — Retention, compression, and knowledge-promotion seam

- [ ] Define retrieval states such as hot, warm, compressed, cold, and
  purgeable separately from administrative/privacy deletion.
- [ ] Preserve pinned revisions and provenance when creating deterministic
  compression or summary derivations.
- [ ] Define a provider-neutral promotion-candidate export for semantic and
  procedural knowledge, including supporting/contradicting evidence, temporal
  validity, and supersession. Do not create a long-term-knowledge repository or
  automatically promote diary entries yet.

### Conversation reference resolution support

Cangjie may provide candidates across observations, decisions, artifacts,
conversation-derived context, and workflow-linked snapshots. Solo owns natural
language interpretation, confidence, ambiguity, and user clarification.

### Semantic replay support

Cangjie does not replay workflows. It supplies immutable revisions,
supersession history, provenance, and snapshot evidence. Solo resolves the
semantic target; Zhinu determines execution dependencies, invalidation, and
replay.

### Optional retrieval extensions

Embedding, hybrid, code-graph, or provider-specific retrieval belongs in
optional packages. LatticeDbSharp is the first graph-provider target, while
embedding generation remains host-supplied and optional.

## Explicit non-goals for the initial foundation

- autonomous extraction from every conversation;
- automatic summarization pipelines;
- arbitrary knowledge-graph infrastructure;
- model-specific embeddings in the core;
- coding-specific core types;
- workflow execution, invalidation, or replay;
- agent orchestration;
- elaborate scoring, forgetting, or decay algorithms;
- distributed persistence or cloud-service architecture;
- migration machinery before persisted user data exists.

## Most important proof

```text
DeepSeek or another producer
    performs repository research
        ↓
Cangjie stores immutable observations and provenance
        ↓ process restart
Claude, Codex, or another consumer
    reconstructs a pinned context snapshot
    performs architecture work
        ↓
Cangjie stores the decision and derivation evidence
        ↓
Zhinu retains exact snapshot and output references
```

If this works without provider-native conversation history, Cangjie has proven
its role as Solo's durable context and memory foundation.
