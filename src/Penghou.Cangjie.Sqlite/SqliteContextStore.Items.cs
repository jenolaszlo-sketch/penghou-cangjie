using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace Penghou.Cangjie.Sqlite;

/// <summary>Context items: store, retrieval by identity/key, history, deletion, and expiry.</summary>
public sealed partial class SqliteContextStore
{
    /// <inheritdoc />
    public async ValueTask<ContextItem> StoreAsync(
        ContextItem item,
        ContextWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateItem(item);
        ValidateWriteOptions(item, options);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
        var normalizedTags = NormalizeTags(item.Tags);
        var normalizedMetadata = new Dictionary<string, string>(
            item.Metadata,
            StringComparer.Ordinal);
        var requestHash = ComputeRequestHash(
            item with
            {
                Id = Guid.Empty,
                Revision = 0,
                Kind = item.Kind.Trim(),
                CreatedAt = default,
                ExpiresAt = item.ExpiresAt?.ToUniversalTime(),
                Provenance = item.Provenance with
                {
                    OriginatedAt = item.Provenance.OriginatedAt?.ToUniversalTime(),
                    Attributes = new Dictionary<string, string>(
                        item.Provenance.Attributes,
                        StringComparer.Ordinal)
                },
                Tags = normalizedTags,
                Metadata = normalizedMetadata
            });
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            if (options?.IdempotencyKey is not null)
            {
                var existing = await ReadByIdempotencyKeyAsync(
                    connection,
                    transaction,
                    item.Scope,
                    options.IdempotencyKey,
                    cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (string.Equals(
                        existing.Value.RequestHash,
                        requestHash,
                        StringComparison.Ordinal))
                    {
                        return existing.Value.Item;
                    }

                    throw new ContextStoreConflictException(
                        $"Idempotency key '{options.IdempotencyKey}' is already associated with different context in scope '{item.Scope}'.");
                }
            }

            if (await ItemExistsAsync(connection, transaction, id, cancellationToken)
                .ConfigureAwait(false))
            {
                throw new ContextStoreConflictException(
                    $"Context item '{id:D}' already exists and immutable revisions cannot be overwritten.");
            }

            var current = item.Key is null
                ? null
                : await ReadCurrentRevisionAsync(
                    connection,
                    transaction,
                    item.Scope,
                    item.Key,
                    cancellationToken).ConfigureAwait(false);
            var actualRevision = current?.Revision ?? 0;
            if (options?.ExpectedRevision is int expectedRevision &&
                expectedRevision != actualRevision)
            {
                throw new ContextStoreConflictException(
                    $"Expected revision {expectedRevision} for '{item.Scope}/{item.Key}', but current revision is {actualRevision}.");
            }

            var normalized = item with
            {
                Id = id,
                Revision = item.Key is null ? 1 : actualRevision + 1,
                Kind = item.Kind.Trim(),
                CreatedAt = item.CreatedAt == default
                    ? timeProvider.GetUtcNow()
                    : item.CreatedAt.ToUniversalTime(),
                ExpiresAt = item.ExpiresAt?.ToUniversalTime(),
                Provenance = item.Provenance with
                {
                    OriginatedAt = item.Provenance.OriginatedAt?.ToUniversalTime(),
                    Attributes = new Dictionary<string, string>(
                        item.Provenance.Attributes,
                        StringComparer.Ordinal)
                },
                Tags = normalizedTags,
                Metadata = normalizedMetadata
            };

            await InsertItemAsync(
                connection,
                transaction,
                normalized,
                options?.IdempotencyKey,
                requestHash,
                cancellationToken).ConfigureAwait(false);
            await ReplaceTagsAsync(
                connection,
                transaction,
                normalized,
                cancellationToken).ConfigureAwait(false);
            await ReplaceFtsAsync(
                connection,
                transaction,
                normalized,
                cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                await InsertRelationAsync(
                    connection,
                    transaction,
                    new ContextRelation
                    {
                        FromId = normalized.Id,
                        ToId = current.Value.Id,
                        Kind = ContextRelationKinds.Supersedes,
                        CreatedAt = normalized.CreatedAt
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            CangjieDiagnostics.ItemsStored.Add(1);
            return normalized;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new ContextStoreConflictException(
                "The context append conflicted with another committed write.",
                exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ContextItem?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Context ID must not be empty.", nameof(id));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ReadSingleAsync(
            connection,
            "ci.id = $id",
            [("$id", id.ToString("D"))],
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ContextItem?> GetLatestByKeyAsync(
        string scope,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ReadSingleAsync(
            connection,
            "ci.scope = $scope AND ci.logical_key = $key",
            [("$scope", scope), ("$key", key)],
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextItem>> GetHistoryByKeyAsync(
        string scope,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ReadManyAsync(
            connection,
            "ci.scope = $scope AND ci.logical_key = $key",
            [("$scope", scope), ("$key", key)],
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Context ID must not be empty.", nameof(id));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        if (await HasLogicalKeyAsync(connection, transaction, id, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new ContextStoreConflictException(
                $"Context item '{id:D}' belongs to an immutable revision history and cannot be deleted by ordinary cleanup.");
        }
        if (await IsSnapshotPinnedAsync(connection, transaction, id, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new ContextStoreConflictException(
                $"Context item '{id:D}' is pinned by an immutable snapshot and cannot be deleted.");
        }
        await ExecuteAsync(connection, transaction,
            "DELETE FROM context_items_fts WHERE item_id = $id;",
            [("$id", id.ToString("D"))], cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction,
            "DELETE FROM context_items WHERE id = $id;",
            [("$id", id.ToString("D"))], cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteExpiredAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        await ExecuteAsync(connection, transaction,
            """
            DELETE FROM context_items_fts
            WHERE item_id IN (
                SELECT id FROM context_items
                WHERE expires_at <= $now AND logical_key IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM context_snapshot_items csi
                      WHERE csi.item_id = context_items.id
                  )
            );
            """, [("$now", now)], cancellationToken).ConfigureAwait(false);
        var deleted = await ExecuteAsync(connection, transaction,
            """
            DELETE FROM context_items
            WHERE expires_at <= $now AND logical_key IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM context_snapshot_items csi
                  WHERE csi.item_id = context_items.id
              );
            """,
            [("$now", now)], cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
            CangjieDiagnostics.ExpiredDeleted.Add(deleted);
        return deleted;
    }

    private static async Task<bool> ItemExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM context_items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is not null;
    }

    private static async Task<bool> HasLogicalKeyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT logical_key IS NOT NULL
            FROM context_items
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> IsSnapshotPinnedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT 1 FROM context_snapshot_items WHERE item_id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) is not null;
    }

    private static async Task<(Guid Id, int Revision)?> ReadCurrentRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string scope,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, revision
            FROM context_items
            WHERE scope = $scope AND logical_key = $key
            ORDER BY revision DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return (Guid.Parse(reader.GetString(0)), reader.GetInt32(1));
    }

    private static async Task<(ContextItem Item, string RequestHash)?>
        ReadByIdempotencyKeyAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string scope,
            string idempotencyKey,
            CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, $"""
            SELECT {SelectColumns}, ci.request_hash
            FROM context_items ci
            WHERE ci.scope = $scope AND ci.idempotency_key = $idempotencyKey
            LIMIT 1;
            """,
            [("$scope", scope), ("$idempotencyKey", idempotencyKey)]);
        command.Transaction = transaction;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return (ReadItem(reader), reader.GetString(17));
    }

    private static async Task InsertItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ContextItem item,
        string? idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction, """
            INSERT INTO context_items
                (id, scope, logical_key, revision, kind, content, source_uri,
                 source_kind, source_hash, producer, producer_version,
                 originated_at, provenance_attributes_json,
                 created_at, expires_at, metadata_json,
                 idempotency_key, request_hash)
            VALUES
                ($id, $scope, $key, $revision, $kind, $content, $sourceUri,
                 $sourceKind, $sourceHash, $producer, $producerVersion,
                 $originatedAt, $provenanceAttributes,
                 $created, $expires, $metadata,
                 $idempotencyKey, $requestHash);
            """,
            [
                ("$id", item.Id.ToString("D")),
                ("$scope", item.Scope),
                ("$key", item.Key),
                ("$revision", item.Revision),
                ("$kind", item.Kind),
                ("$content", item.Content),
                ("$sourceUri", item.Provenance.Source?.Uri),
                ("$sourceKind", item.Provenance.Source?.Kind),
                ("$sourceHash", item.Provenance.Source?.ContentHash),
                ("$producer", item.Provenance.Producer),
                ("$producerVersion", item.Provenance.ProducerVersion),
                ("$originatedAt", item.Provenance.OriginatedAt is null
                    ? null
                    : FormatTimestamp(item.Provenance.OriginatedAt.Value)),
                ("$provenanceAttributes", JsonSerializer.Serialize(
                    item.Provenance.Attributes)),
                ("$created", FormatTimestamp(item.CreatedAt)),
                ("$expires", item.ExpiresAt is null
                    ? null
                    : FormatTimestamp(item.ExpiresAt.Value)),
                ("$metadata", JsonSerializer.Serialize(item.Metadata)),
                ("$idempotencyKey", idempotencyKey),
                ("$requestHash", requestHash)
            ], cancellationToken).ConfigureAwait(false);

    private static async Task ReplaceTagsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ContextItem item,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction,
            "DELETE FROM context_tags WHERE item_id = $id;",
            [("$id", item.Id.ToString("D"))], cancellationToken).ConfigureAwait(false);
        foreach (var tag in item.Tags)
        {
            await ExecuteAsync(connection, transaction,
                "INSERT INTO context_tags(item_id, tag) VALUES ($id, $tag);",
                [("$id", item.Id.ToString("D")), ("$tag", tag)], cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ReplaceFtsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ContextItem item,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction,
            "DELETE FROM context_items_fts WHERE item_id = $id;",
            [("$id", item.Id.ToString("D"))], cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO context_items_fts(item_id, content) VALUES ($id, $content);",
            [("$id", item.Id.ToString("D")), ("$content", item.Content)], cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ContextItem?> ReadSingleAsync(
        SqliteConnection connection,
        string predicate,
        IReadOnlyCollection<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken)
    {
        var values = await ReadManyAsync(
            connection,
            predicate,
            parameters,
            1,
            cancellationToken).ConfigureAwait(false);
        return values.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<ContextItem>> ReadManyAsync(
        SqliteConnection connection,
        string predicate,
        IReadOnlyCollection<(string Name, object? Value)> parameters,
        int? limit,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT {SelectColumns}
            FROM context_items ci
            WHERE {predicate}
            ORDER BY ci.revision DESC, ci.created_at DESC, ci.id ASC
            {(limit is null ? string.Empty : "LIMIT $limit")};
            """;
        var allParameters = parameters.ToList();
        if (limit is not null)
            allParameters.Add(("$limit", limit.Value));
        await using var command = CreateCommand(connection, sql, allParameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var values = new List<ContextItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            values.Add(ReadItem(reader));
        return values;
    }

    private static ContextItem ReadItem(SqliteDataReader reader)
    {
        var provenanceAttributes = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(12)) ?? [];
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(15)) ?? [];
        var tags = JsonSerializer.Deserialize<string[]>(reader.GetString(16)) ?? [];
        return new ContextItem
        {
            Id = Guid.Parse(reader.GetString(0)),
            Scope = reader.GetString(1),
            Key = reader.IsDBNull(2) ? null : reader.GetString(2),
            Revision = reader.GetInt32(3),
            Kind = reader.GetString(4),
            Content = reader.GetString(5),
            Provenance = new ContextProvenance
            {
                Source = reader.IsDBNull(6)
                    ? null
                    : new ContextSource
                    {
                        Uri = reader.GetString(6),
                        Kind = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ContentHash = reader.IsDBNull(8) ? null : reader.GetString(8)
                    },
                Producer = reader.GetString(9),
                ProducerVersion = reader.IsDBNull(10) ? null : reader.GetString(10),
                OriginatedAt = reader.IsDBNull(11)
                    ? null
                    : ParseTimestamp(reader.GetString(11)),
                Attributes = provenanceAttributes
            },
            CreatedAt = ParseTimestamp(reader.GetString(13)),
            ExpiresAt = reader.IsDBNull(14)
                ? null
                : ParseTimestamp(reader.GetString(14)),
            Metadata = metadata,
            Tags = tags
        };
    }

    private static string ComputeRequestHash(ContextItem item)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            item.Scope,
            item.Key,
            item.Kind,
            item.Content,
            Provenance = new
            {
                Source = item.Provenance.Source is null
                ? null
                : new
                {
                    item.Provenance.Source.Uri,
                    item.Provenance.Source.Kind,
                    item.Provenance.Source.ContentHash
                },
                item.Provenance.Producer,
                item.Provenance.ProducerVersion,
                item.Provenance.OriginatedAt,
                Attributes = item.Provenance.Attributes.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal)
            },
            item.ExpiresAt,
            Metadata = item.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            Tags = item.Tags.OrderBy(tag => tag, StringComparer.Ordinal)
        });
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
