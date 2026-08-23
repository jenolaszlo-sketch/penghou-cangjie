using Microsoft.Data.Sqlite;

using System.Text.Json;

namespace Penghou.Cangjie.Sqlite;

/// <summary>Immutable snapshots: atomic pinning and recorded-order resolution.</summary>
public sealed partial class SqliteContextStore
{
    /// <inheritdoc />
    public async ValueTask<ContextSnapshot> StoreSnapshotAsync(
        ContextSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ValidateSnapshot(snapshot);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var stored = snapshot with
        {
            Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id,
            SelectedAt = snapshot.SelectedAt == default
                ? timeProvider.GetUtcNow()
                : snapshot.SelectedAt.ToUniversalTime(),
            ItemIds = snapshot.ItemIds.ToArray(),
            Metadata = new Dictionary<string, string>(snapshot.Metadata, StringComparer.Ordinal)
        };
        try
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO context_snapshots
                    (id, query_identity, strategy, strategy_version, selected_at, purpose, metadata_json)
                VALUES
                    ($id, $queryIdentity, $strategy, $strategyVersion, $selectedAt, $purpose, $metadata);
                """,
                [("$id", stored.Id.ToString("D")), ("$queryIdentity", stored.QueryIdentity),
                 ("$strategy", stored.Strategy), ("$strategyVersion", stored.StrategyVersion),
                 ("$selectedAt", FormatTimestamp(stored.SelectedAt)), ("$purpose", stored.Purpose),
                 ("$metadata", JsonSerializer.Serialize(stored.Metadata))], cancellationToken)
                .ConfigureAwait(false);
            for (var index = 0; index < stored.ItemIds.Count; index++)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO context_snapshot_items(snapshot_id, item_id, ordinal)
                    VALUES ($snapshotId, $itemId, $ordinal);
                    """,
                    [("$snapshotId", stored.Id.ToString("D")),
                     ("$itemId", stored.ItemIds[index].ToString("D")), ("$ordinal", index)],
                    cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return stored;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new ContextStoreConflictException(
                "Snapshot creation conflicted with an existing identity or missing context item.",
                exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ContextSnapshot?> GetSnapshotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Snapshot ID must not be empty.", nameof(id));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadSnapshotAsync(connection, id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ContextSnapshotResolution?> ResolveSnapshotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Snapshot ID must not be empty.", nameof(id));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await ReadSnapshotAsync(connection, id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        await using var command = CreateCommand(connection, $"""
            SELECT {SelectColumns}
            FROM context_snapshot_items csi
            JOIN context_items ci ON ci.id = csi.item_id
            WHERE csi.snapshot_id = $id
            ORDER BY csi.ordinal ASC;
            """, [("$id", id.ToString("D"))]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<ContextItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(ReadItem(reader));
        return new ContextSnapshotResolution { Snapshot = snapshot, Items = items };
    }

    private static async Task<ContextSnapshot?> ReadSnapshotAsync(
        SqliteConnection connection,
        Guid id,
        CancellationToken cancellationToken)
    {
        string queryIdentity;
        string strategy;
        string strategyVersion;
        DateTimeOffset selectedAt;
        string? purpose;
        IReadOnlyDictionary<string, string> metadata;
        await using (var command = CreateCommand(connection, """
            SELECT query_identity, strategy, strategy_version, selected_at,
                   purpose, metadata_json
            FROM context_snapshots
            WHERE id = $id;
            """, [("$id", id.ToString("D"))]))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            queryIdentity = reader.GetString(0);
            strategy = reader.GetString(1);
            strategyVersion = reader.GetString(2);
            selectedAt = ParseTimestamp(reader.GetString(3));
            purpose = reader.IsDBNull(4) ? null : reader.GetString(4);
            metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(5)) ?? [];
        }
        await using var itemCommand = CreateCommand(connection, """
            SELECT item_id FROM context_snapshot_items
            WHERE snapshot_id = $id ORDER BY ordinal ASC;
            """, [("$id", id.ToString("D"))]);
        await using var itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var itemIds = new List<Guid>();
        while (await itemReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            itemIds.Add(Guid.Parse(itemReader.GetString(0)));
        return new ContextSnapshot
        {
            Id = id,
            ItemIds = itemIds,
            QueryIdentity = queryIdentity,
            Strategy = strategy,
            StrategyVersion = strategyVersion,
            SelectedAt = selectedAt,
            Purpose = purpose,
            Metadata = metadata
        };
    }

    private static void ValidateSnapshot(ContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.ItemIds);
        if (snapshot.ItemIds.Count == 0)
            throw new ArgumentException("A snapshot must reference at least one item.", nameof(snapshot));
        if (snapshot.ItemIds.Any(id => id == Guid.Empty) ||
            snapshot.ItemIds.Distinct().Count() != snapshot.ItemIds.Count)
        {
            throw new ArgumentException("Snapshot item IDs must be non-empty and unique.", nameof(snapshot));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.QueryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.Strategy);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.StrategyVersion);
        ArgumentNullException.ThrowIfNull(snapshot.Metadata);
        if (snapshot.Metadata.Any(pair =>
            string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
        {
            throw new ArgumentException("Snapshot metadata must contain non-blank keys and non-null values.", nameof(snapshot));
        }
    }
}
