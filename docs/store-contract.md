# IContextStore semantic contract

This document defines behavior shared by every `IContextStore`
implementation. Provider-specific storage details are not part of the contract.

## Immutable records

- A successful append returns a stable non-empty physical ID.
- Stored semantic content cannot be replaced under an existing physical ID.
- Unkeyed items receive revision 1.
- Keyed revisions increase monotonically within an exact `(scope, key)` pair.
- Appending a keyed revision atomically creates its `supersedes` link to the
  previous revision.
- History is returned newest revision first.

## Optimistic concurrency

`ExpectedRevision` is the caller's precondition on the current keyed revision.
Zero means no revision is expected. A mismatch throws
`ContextStoreConflictException` and commits no partial item, tag, index, or
relation state.

An omitted expected revision permits an unconditional append. Implementations
must still allocate a unique next revision atomically.

## Idempotency

An idempotency key is optional and unique within an exact scope.

- An equivalent retry returns the originally committed `ContextItem`.
- The retry may occur through a fresh store instance.
- Conflicting semantic content with the same scoped key throws
  `ContextStoreConflictException`.
- Physical ID, assigned revision, and store-assigned creation time are results,
  not inputs to semantic retry equivalence.

## Provenance

Every item has a non-blank producer. Source and producer are distinct: source
describes where information originated, while producer describes what recorded
or derived the context item. Provenance round-trips without requiring model or
provider-native message types.

## Relations

Relations are directed and idempotent by `(from ID, to ID, kind)`. Implementations
support outgoing, incoming, and both-direction retrieval with deterministic
ordering. `QueryRelationsAsync` accepts exact kind filters and a required bound
from 1 through 100. Relation kinds are extensible strings.

`GetRelatedItemsAsync` is an implementation-neutral projection that resolves
the context item at each relation's opposite end. A dangling relation is an
integrity error and must not be silently omitted.

## Search

- Scope and logical key are exact filters.
- `Scopes` is an ordered set of exact scopes in descending precedence and is
  mutually exclusive with `Scope`.
- Layered retrieval returns a keyed logical concept once, choosing its
  highest-precedence requested scope; unkeyed physical items remain distinct.
- Requested tags use all-tags semantics.
- Kinds are exact extensible identifiers.
- Limits are enforced and bounded by the contract.
- Expired items are excluded unless explicitly requested.
- Equal matches have deterministic ordering.
- Result rank is local to one query result set.
- Every result identifies its retrieval strategy and implementation-defined
  strategy version. Optional scores are local to that exact strategy/version
  and must not be compared across either boundary.

Search implementations emit `context.search` activities through
`CangjieDiagnostics.ActivitySource`. Built-in tags contain only strategy,
boolean flags, limits, and counts. Query text, context content, scope values,
logical keys, source URIs, and tag values are not recorded.

## Retention

Ordinary physical deletion is permitted for standalone records. Keyed revision
history is protected from ordinary deletion and expiration cleanup. Future
snapshot pinning may strengthen retention further.

## Conformance suite

Store packages implement `IContextStoreFixture` from
`Penghou.Cangjie.Testing`:

```csharp
public interface IContextStoreFixture : IAsyncDisposable
{
    IContextStore Store { get; }
    IContextStore CreatePeerStore();
}
```

`CreatePeerStore` must return a fresh instance over the same durable backing
data. `ContextStoreConformanceSuite.VerifyAsync` currently verifies:

- generated physical identity and standalone revision;
- peer-store restart round trip;
- keyed revision ordering and automatic supersession;
- optimistic and physical-immutability conflicts;
- equivalent and conflicting idempotent retries across peers;
- relation persistence, both traversal directions, exact kind filtering,
  bounds, and related-item projection;
- exact scope/kind lexical retrieval isolation and ordered multi-scope
  precedence with logical-key deduplication.
- retrieval strategy metadata.

The suite throws `InvalidOperationException` naming the failed check. It does
not depend on a particular test framework, so implementations can call it from
xUnit, NUnit, MSTest, or their own certification harness.
