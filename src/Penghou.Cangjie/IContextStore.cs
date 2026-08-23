namespace Penghou.Cangjie;

/// <summary>Stores and explicitly retrieves attributable application context.</summary>
public interface IContextStore
{
    /// <summary>Appends one immutable item revision atomically.</summary>
    ValueTask<ContextItem> StoreAsync(
        ContextItem item,
        ContextWriteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an item by physical identity, including an expired item.</summary>
    ValueTask<ContextItem?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the most recently created revision for an exact scope and logical key.</summary>
    ValueTask<ContextItem?> GetLatestByKeyAsync(
        string scope,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Gets all revisions for an exact scope and logical key, newest first.</summary>
    ValueTask<IReadOnlyList<ContextItem>> GetHistoryByKeyAsync(
        string scope,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Searches content and indexed filters deterministically.</summary>
    ValueTask<IReadOnlyList<ContextSearchHit>> SearchAsync(
        ContextQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically stores an immutable snapshot and pins its items.</summary>
    ValueTask<ContextSnapshot> StoreSnapshotAsync(ContextSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a snapshot by identity.</summary>
    ValueTask<ContextSnapshot?> GetSnapshotAsync(Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a snapshot's exact items in recorded order.</summary>
    ValueTask<ContextSnapshotResolution?> ResolveSnapshotAsync(Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Physically deletes a standalone item and every relation involving it.
    /// Keyed revisions are protected from ordinary deletion.
    /// </summary>
    ValueTask DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a directed relation idempotently.</summary>
    ValueTask AddRelationAsync(
        ContextRelation relation,
        CancellationToken cancellationToken = default);

    /// <summary>Gets directed relationships involving an item in the requested direction.</summary>
    ValueTask<IReadOnlyList<ContextRelation>> GetRelationsAsync(
        Guid id,
        ContextRelationDirection direction =
            ContextRelationDirection.Outgoing,
        CancellationToken cancellationToken = default);

    /// <summary>Queries bounded directed relationships with exact kind filters.</summary>
    ValueTask<IReadOnlyList<ContextRelation>> QueryRelationsAsync(
        Guid id,
        ContextRelationQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Physically deletes expired standalone items. Keyed revisions remain
    /// retained but hidden from ordinary search.
    /// </summary>
    ValueTask<int> DeleteExpiredAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a safe health probe: verifies the backing store can be opened,
    /// its schema is compatible, and a trivial read succeeds. Readiness
    /// endpoints use this; it must never mutate state.
    /// </summary>
    ValueTask<ContextStoreHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default);
}
