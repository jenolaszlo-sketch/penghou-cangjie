namespace Penghou.Cangjie;

/// <summary>Stores and explicitly retrieves attributable application context.</summary>
public interface IContextStore
{
    /// <summary>Creates or replaces an item atomically and returns its stored representation.</summary>
    ValueTask<ContextItem> StoreAsync(
        ContextItem item,
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

    /// <summary>Physically deletes an item and every relation involving it.</summary>
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

    /// <summary>Physically deletes every item whose expiration time has passed.</summary>
    ValueTask<int> DeleteExpiredAsync(
        CancellationToken cancellationToken = default);
}
