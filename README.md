# Penghou.Cangjie

Penghou.Cangjie is a lightweight, local-first, provenance-aware context store
for .NET AI applications. It provides explicit persistent context, SQLite FTS5
retrieval, scopes, tags, relationships, logical history, and evidence tracking
without requiring an agent framework, vector database, or LLM.

## Packages

| Package | Purpose |
| --- | --- |
| `Penghou.Cangjie` | Context, provenance, relation, query, and store abstractions |
| `Penghou.Cangjie.Sqlite` | Transactional SQLite and FTS5 implementation |
| `Penghou.Cangjie.Testing` | Reusable `IContextStore` conformance suite |

## Install

```bash
dotnet add package Penghou.Cangjie.Sqlite --prerelease
```

## Quick start

```csharp
using Penghou.Cangjie;
using Penghou.Cangjie.Sqlite;

IContextStore store = new SqliteContextStore(new CangjieSqliteOptions
{
    DatabasePath = "context.db"
});

var evidence = await store.StoreAsync(new ContextItem
{
    Scope = "repo:my-app",
    Kind = ContextKinds.Evidence,
    Content = "The gateway references Yarp.ReverseProxy.",
    Provenance = new ContextProvenance
    {
        Producer = "solo:research",
        Source = new ContextSource
        {
            Uri = "repo://src/Gateway/Gateway.csproj"
        }
    },
    Tags = ["gateway", "architecture"]
});

var results = await store.SearchAsync(new ContextQuery
{
    Scope = "repo:my-app",
    Text = "reverse proxy",
    Tags = ["architecture"]
});
```

Direct construction does not require dependency injection. Applications using
Microsoft.Extensions.DependencyInjection can register the store with:

```csharp
services.AddCangjieSqlite(options =>
{
    options.DatabasePath = "context.db";
});
```

## Provenance and long-horizon history

`ContextItem.Id` identifies one immutable observation or revision. The optional
`Key` identifies the logical concept across revisions. Append changed decisions
and knowledge as new items, then connect them explicitly:

```csharp
await store.AddRelationAsync(new ContextRelation
{
    FromId = revisedDecision.Id,
    ToId = originalDecision.Id,
    Kind = ContextRelationKinds.Supersedes
});
```

Use `GetLatestByKeyAsync` for the current revision and
`GetHistoryByKeyAsync` for deterministic history. Each physical item is
immutable. Store a changed logical concept as a new item with the same scope
and key; Cangjie assigns the next revision and atomically links it to its
predecessor with `supersedes`. Use `ExpectedRevision` for optimistic concurrency
and a scoped `IdempotencyKey` for safe ingestion retries.

Relation kinds are persisted as text. Cangjie provides well-known provenance
kinds while allowing applications and future extension packages to use their
own stable identifiers.

## Search semantics

- Scope and logical-key matching are exact.
- All requested tags must be present; tags are normalized to lowercase.
- Empty `Text` performs indexed filtering without FTS.
- User text is tokenized and escaped before reaching FTS5.
- Equal matches are ordered by creation time descending, then ID ascending.
- Expired items are hidden from search by default but remain available by ID.
- `Rank` is the one-based position within the returned result set, not a
  globally comparable score.

## What Cangjie is not

- Not an agent framework
- Not an orchestration or workflow engine
- Not a vector database
- Not a chatbot history or persona framework
- Not an autonomous memory extraction system
- Not a typed workflow artifact store
- Not automatic prompt injection

The caller decides what context means, what to store, what to retrieve, and
what belongs in a model prompt.

## Architecture

```text
Application / Agent / Workflow
            |
            | explicit store/search
            v
     Penghou.Cangjie
            |
            v
         SQLite
         |- records and scopes
         |- provenance relations
         |- tags and expiration
         `- FTS5 lexical search
```

## Roadmap

See the [project roadmap](ROADMAP.md) for immutable logical revisions,
first-class provenance, scoped retrieval, pinned context snapshots, and the
planned Solo/Zhinu integration proof.

The `Penghou.Cangjie.Integration.Tests` project contains that restart-safe
reference-flow proof without adding Solo or Zhinu dependencies to Cangjie core.

Potential future extension packages include hybrid embedding retrieval and a
snapshot-aware code graph extracted through Roslyn. These remain separate from
the small lexical core, and Cangjie will not introduce an LLM dependency.

## Design documents

- [Architecture](docs/architecture.md)
- [`IContextStore` contract](docs/store-contract.md)
- [Roadmap](ROADMAP.md)

## License

MIT
