namespace Penghou.Cangjie;

/// <summary>Provides implementation-neutral relation projections.</summary>
public static class ContextStoreRelationExtensions
{
    /// <summary>Gets bounded relations and resolves each opposite context item.</summary>
    public static async ValueTask<IReadOnlyList<ContextRelatedItem>>
        GetRelatedItemsAsync(
            this IContextStore store,
            Guid id,
            ContextRelationQuery? query = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (id == Guid.Empty)
            throw new ArgumentException("Context ID must not be empty.", nameof(id));

        var relations = await store.QueryRelationsAsync(
            id,
            query ?? new ContextRelationQuery(),
            cancellationToken).ConfigureAwait(false);
        var results = new List<ContextRelatedItem>(relations.Count);
        foreach (var relation in relations)
        {
            var relatedId = relation.FromId == id
                ? relation.ToId
                : relation.FromId;
            var item = await store.GetAsync(relatedId, cancellationToken)
                .ConfigureAwait(false);
            if (item is null)
            {
                throw new InvalidOperationException(
                    $"Relation from '{relation.FromId:D}' to '{relation.ToId:D}' references missing context '{relatedId:D}'.");
            }

            results.Add(new ContextRelatedItem
            {
                Relation = relation,
                Item = item
            });
        }

        return results;
    }
}
