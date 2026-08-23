namespace Penghou.Cangjie.Testing;

using System.Text.Json;

/// <summary>Verifies durable semantic invariants required of every context store.</summary>
public static class ContextStoreConformanceSuite
{
    /// <summary>Runs the reusable conformance checks against a store fixture.</summary>
    public static async Task<ContextStoreConformanceReport> VerifyAsync(
        IContextStoreFixture fixture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var checks = new List<string>();
        var scope = $"conformance:{Guid.NewGuid():N}";
        var store = fixture.Store;

        var standalone = await store.StoreAsync(
            Item(scope, "standalone", "custom:observation"),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Require(standalone.Id != Guid.Empty, "store.generated-id");
        Require(standalone.Revision == 1, "store.standalone-revision");
        checks.Add("store-round-trip");

        var peer = fixture.CreatePeerStore();
        var reopened = await peer.GetAsync(standalone.Id, cancellationToken)
            .ConfigureAwait(false);
        Require(Equivalent(reopened, standalone), "restart.round-trip");
        checks.Add("restart-persistence");

        const string logicalKey = "decision:storage";
        var first = await store.StoreAsync(
            Item(scope, "revision one", ContextKinds.Decision) with
            {
                Key = logicalKey
            },
            new ContextWriteOptions { ExpectedRevision = 0 },
            cancellationToken).ConfigureAwait(false);
        var second = await store.StoreAsync(
            Item(scope, "revision two", ContextKinds.Decision) with
            {
                Key = logicalKey
            },
            new ContextWriteOptions { ExpectedRevision = 1 },
            cancellationToken).ConfigureAwait(false);
        Require(first.Revision == 1 && second.Revision == 2, "revision.sequence");
        var history = await peer.GetHistoryByKeyAsync(
            scope,
            logicalKey,
            cancellationToken).ConfigureAwait(false);
        Require(
            history.Count == 2 && history[0].Id == second.Id && history[1].Id == first.Id,
            "revision.history");
        var supersession = await peer.GetRelationsAsync(
            second.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Require(
            supersession.Any(relation =>
                relation.ToId == first.Id &&
                relation.Kind == ContextRelationKinds.Supersedes),
            "revision.supersession");
        checks.Add("immutable-revisions");

        await RequireConflictAsync(
            () => store.StoreAsync(
                Item(scope, "stale revision", ContextKinds.Decision) with
                {
                    Key = logicalKey
                },
                new ContextWriteOptions { ExpectedRevision = 1 },
                cancellationToken).AsTask(),
            "revision.optimistic-concurrency").ConfigureAwait(false);
        await RequireConflictAsync(
            () => store.StoreAsync(
                standalone with { Revision = 0, Content = "mutated" },
                cancellationToken: cancellationToken).AsTask(),
            "revision.physical-immutability").ConfigureAwait(false);
        checks.Add("write-conflicts");

        var idempotentItem = Item(scope, "idempotent", ContextKinds.Evidence);
        var idempotency = new ContextWriteOptions
        {
            IdempotencyKey = "producer:operation-1"
        };
        var idempotentFirst = await store.StoreAsync(
            idempotentItem,
            idempotency,
            cancellationToken).ConfigureAwait(false);
        var idempotentRetry = await peer.StoreAsync(
            idempotentItem,
            idempotency,
            cancellationToken).ConfigureAwait(false);
        Require(
            Equivalent(idempotentRetry, idempotentFirst),
            "idempotency.equivalent-retry");
        await RequireConflictAsync(
            () => store.StoreAsync(
                idempotentItem with { Content = "conflicting" },
                idempotency,
                cancellationToken).AsTask(),
            "idempotency.conflicting-reuse").ConfigureAwait(false);
        checks.Add("scoped-idempotency");

        var related = await store.StoreAsync(
            Item(scope, "related", ContextKinds.Knowledge),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await store.AddRelationAsync(
            new ContextRelation
            {
                FromId = related.Id,
                ToId = standalone.Id,
                Kind = "conformance:relates-to"
            },
            cancellationToken).ConfigureAwait(false);
        var otherRelated = await store.StoreAsync(
            Item(scope, "other related", ContextKinds.Knowledge),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await store.AddRelationAsync(
            new ContextRelation
            {
                FromId = related.Id,
                ToId = otherRelated.Id,
                Kind = ContextRelationKinds.Supports
            },
            cancellationToken).ConfigureAwait(false);
        var outgoing = await peer.GetRelationsAsync(
            related.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var incoming = await peer.GetRelationsAsync(
            standalone.Id,
            ContextRelationDirection.Incoming,
            cancellationToken).ConfigureAwait(false);
        Require(outgoing.Count == 2 && incoming.Count == 1, "relations.directions");
        var filtered = await peer.QueryRelationsAsync(
            related.Id,
            new ContextRelationQuery
            {
                Kinds = ["conformance:relates-to"],
                Limit = 1
            },
            cancellationToken).ConfigureAwait(false);
        Require(
            filtered.Count == 1 &&
            filtered[0].Kind == "conformance:relates-to",
            "relations.filtered-bounded-query");
        var relatedItems = await peer.GetRelatedItemsAsync(
            related.Id,
            new ContextRelationQuery
            {
                Kinds = [ContextRelationKinds.Supports]
            },
            cancellationToken).ConfigureAwait(false);
        Require(
            relatedItems.Count == 1 &&
            relatedItems[0].Item.Id == otherRelated.Id,
            "relations.related-item-projection");
        checks.Add("relation-persistence");

        await store.StoreAsync(
            Item($"{scope}:isolated", "lexical needle", ContextKinds.Evidence),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var search = await peer.SearchAsync(
            new ContextQuery
            {
                Scope = scope,
                Text = "lexical needle",
                Kinds = ["custom:observation"],
                Limit = 10
            },
            cancellationToken).ConfigureAwait(false);
        Require(search.Count == 1 && search[0].Item.Id == standalone.Id, "search.filters");
        Require(
            search[0].Strategy == ContextSearchStrategies.Lexical &&
            !string.IsNullOrWhiteSpace(search[0].StrategyVersion),
            "search.strategy-metadata");
        var fallbackScope = $"{scope}:fallback";
        var unrelatedScope = $"{scope}:unrelated";
        var preferredConcept = await store.StoreAsync(
            Item(scope, "preferred concept", ContextKinds.Knowledge) with
            {
                Key = "shared:concept"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await store.StoreAsync(
            Item(fallbackScope, "fallback concept", ContextKinds.Knowledge) with
            {
                Key = "shared:concept"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var fallbackOnly = await store.StoreAsync(
            Item(fallbackScope, "fallback only", ContextKinds.Knowledge) with
            {
                Key = "fallback:only"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await store.StoreAsync(
            Item(unrelatedScope, "unrelated", ContextKinds.Knowledge),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var layered = await peer.SearchAsync(
            new ContextQuery
            {
                Scopes = [scope, fallbackScope],
                Kinds = [ContextKinds.Knowledge],
                Limit = 10
            },
            cancellationToken).ConfigureAwait(false);
        Require(
            layered.Count(hit => hit.Item.Key == "shared:concept") == 1 &&
            layered.Any(hit => hit.Item.Id == preferredConcept.Id) &&
            layered.Any(hit => hit.Item.Id == fallbackOnly.Id) &&
            layered.All(hit => hit.Item.Scope != unrelatedScope),
            "search.ordered-scopes");
        checks.Add("scoped-retrieval");

        return new ContextStoreConformanceReport
        {
            CompletedChecks = checks
        };
    }

    private static ContextItem Item(string scope, string content, string kind) =>
        new()
        {
            Scope = scope,
            Kind = kind,
            Content = content.Contains("needle", StringComparison.Ordinal)
                ? content
                : $"{content} lexical needle",
            Provenance = new ContextProvenance
            {
                Producer = "cangjie:conformance-suite",
                ProducerVersion = "1"
            }
        };

    private static async Task RequireConflictAsync(
        Func<Task> operation,
        string check)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (ContextStoreConflictException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Context store conformance check '{check}' expected a ContextStoreConflictException.");
    }

    private static void Require(bool condition, string check)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Context store conformance check '{check}' failed.");
        }
    }

    private static bool Equivalent(ContextItem? left, ContextItem right) =>
        left is not null &&
        string.Equals(
            JsonSerializer.Serialize(left),
            JsonSerializer.Serialize(right),
            StringComparison.Ordinal);
}
