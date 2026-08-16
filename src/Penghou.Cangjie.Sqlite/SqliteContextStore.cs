using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Penghou.Cangjie.Sqlite;

/// <summary>
/// Implements transactional local context storage and safe FTS5 retrieval.
/// </summary>
public sealed class SqliteContextStore : IContextStore
{
    private readonly CangjieSqliteOptions options;
    private readonly TimeProvider timeProvider;
    private readonly string connectionString;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool initialized;

    /// <summary>Initializes a store for a SQLite database file.</summary>
    /// <param name="options">SQLite persistence options.</param>
    /// <param name="timeProvider">Optional clock used for timestamps and expiration.</param>
    public SqliteContextStore(
        CangjieSqliteOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);
        if (options.BusyTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));

        this.options = options;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        var path = Path.GetFullPath(options.DatabasePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    /// <inheritdoc />
    public async ValueTask<ContextItem> StoreAsync(
        ContextItem item,
        CancellationToken cancellationToken = default)
    {
        ValidateItem(item);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        var existingCreatedAt = await ReadCreatedAtAsync(
            connection,
            transaction,
            id,
            cancellationToken).ConfigureAwait(false);
        var createdAt = existingCreatedAt ??
            (item.CreatedAt == default
                ? timeProvider.GetUtcNow()
                : item.CreatedAt.ToUniversalTime());
        var normalized = item with
        {
            Id = id,
            CreatedAt = createdAt,
            ExpiresAt = item.ExpiresAt?.ToUniversalTime(),
            Tags = NormalizeTags(item.Tags),
            Metadata = new Dictionary<string, string>(
                item.Metadata,
                StringComparer.Ordinal)
        };

        await UpsertItemAsync(
            connection,
            transaction,
            normalized,
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
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return normalized;
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
    public async ValueTask<IReadOnlyList<ContextSearchHit>> SearchAsync(
        ContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        var ftsQuery = SafeFtsQueryBuilder.Build(query.Text, query.SearchMode);
        if (!string.IsNullOrWhiteSpace(query.Text) && ftsQuery is null)
            return [];

        var parameters = new List<(string Name, object? Value)>();
        var where = new List<string>();
        if (query.Scope is not null)
        {
            where.Add("ci.scope = $scope");
            parameters.Add(("$scope", query.Scope));
        }
        if (query.Key is not null)
        {
            where.Add("ci.logical_key = $key");
            parameters.Add(("$key", query.Key));
        }
        if (query.SourceUri is not null)
        {
            where.Add("ci.source_uri = $sourceUri");
            parameters.Add(("$sourceUri", query.SourceUri));
        }
        if (!query.IncludeExpired)
        {
            where.Add("(ci.expires_at IS NULL OR ci.expires_at > $now)");
            parameters.Add(("$now", FormatTimestamp(timeProvider.GetUtcNow())));
        }
        AddKindFilter(query.Kinds, where, parameters);
        AddTagFilters(query.Tags, where, parameters);
        if (ftsQuery is not null)
        {
            where.Add("context_items_fts MATCH $fts");
            parameters.Add(("$fts", ftsQuery));
        }
        parameters.Add(("$limit", query.Limit));

        var join = ftsQuery is null
            ? string.Empty
            : "JOIN context_items_fts ON context_items_fts.item_id = ci.id";
        var order = ftsQuery is null
            ? "ci.created_at DESC, ci.id ASC"
            : "bm25(context_items_fts), ci.created_at DESC, ci.id ASC";
        var sql = $"""
            SELECT {SelectColumns}
            FROM context_items ci
            {join}
            WHERE {(where.Count == 0 ? "1 = 1" : string.Join(" AND ", where))}
            ORDER BY {order}
            LIMIT $limit;
            """;
        await using var command = CreateCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<ContextSearchHit>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ContextSearchHit
            {
                Item = ReadItem(reader),
                Rank = results.Count + 1
            });
        }
        return results;
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
        await ExecuteAsync(connection, transaction,
            "DELETE FROM context_items_fts WHERE item_id = $id;",
            [("$id", id.ToString("D"))], cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction,
            "DELETE FROM context_items WHERE id = $id;",
            [("$id", id.ToString("D"))], cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AddRelationAsync(
        ContextRelation relation,
        CancellationToken cancellationToken = default)
    {
        ValidateRelation(relation);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        var createdAt = relation.CreatedAt == default
            ? timeProvider.GetUtcNow()
            : relation.CreatedAt.ToUniversalTime();
        await ExecuteAsync(connection, null,
            """
            INSERT OR IGNORE INTO context_relations
                (from_id, to_id, kind, created_at)
            VALUES ($from, $to, $kind, $created);
            """,
            [
                ("$from", relation.FromId.ToString("D")),
                ("$to", relation.ToId.ToString("D")),
                ("$kind", relation.Kind.Trim()),
                ("$created", FormatTimestamp(createdAt))
            ], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextRelation>> GetRelationsAsync(
        Guid id,
        ContextRelationDirection direction = ContextRelationDirection.Outgoing,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Context ID must not be empty.", nameof(id));
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        var predicate = direction switch
        {
            ContextRelationDirection.Outgoing => "from_id = $id",
            ContextRelationDirection.Incoming => "to_id = $id",
            ContextRelationDirection.Both => "from_id = $id OR to_id = $id",
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        await using var command = CreateCommand(connection, $"""
            SELECT from_id, to_id, kind, created_at
            FROM context_relations
            WHERE {predicate}
            ORDER BY created_at ASC, from_id ASC, to_id ASC, kind ASC;
            """, [("$id", id.ToString("D"))]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var values = new List<ContextRelation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new ContextRelation
            {
                FromId = Guid.Parse(reader.GetString(0)),
                ToId = Guid.Parse(reader.GetString(1)),
                Kind = reader.GetString(2),
                CreatedAt = ParseTimestamp(reader.GetString(3))
            });
        }
        return values;
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
                SELECT id FROM context_items WHERE expires_at <= $now
            );
            """, [("$now", now)], cancellationToken).ConfigureAwait(false);
        var deleted = await ExecuteAsync(connection, transaction,
            "DELETE FROM context_items WHERE expires_at <= $now;",
            [("$now", now)], cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;
        await initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
                return;
            await using var connection = await OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            if (options.EnableWal)
                await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;", [], cancellationToken)
                    .ConfigureAwait(false);
            await ExecuteAsync(connection, null, Schema, [], cancellationToken)
                .ConfigureAwait(false);
            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT MAX(version) FROM cangjie_schema;";
            var version = Convert.ToInt32(
                await versionCommand.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (version != 1)
            {
                throw new InvalidOperationException(
                    $"Cangjie database schema version {version} is not supported by this package.");
            }
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON;", [], cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, null,
            $"PRAGMA busy_timeout = {(long)options.BusyTimeout.TotalMilliseconds};",
            [], cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<DateTimeOffset?> ReadCreatedAtAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT created_at FROM context_items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is string timestamp ? ParseTimestamp(timestamp) : null;
    }

    private static async Task UpsertItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ContextItem item,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction, """
            INSERT INTO context_items
                (id, scope, logical_key, kind, content, source_uri,
                 source_kind, source_hash, created_at, expires_at, metadata_json)
            VALUES
                ($id, $scope, $key, $kind, $content, $sourceUri,
                 $sourceKind, $sourceHash, $created, $expires, $metadata)
            ON CONFLICT(id) DO UPDATE SET
                scope = excluded.scope,
                logical_key = excluded.logical_key,
                kind = excluded.kind,
                content = excluded.content,
                source_uri = excluded.source_uri,
                source_kind = excluded.source_kind,
                source_hash = excluded.source_hash,
                expires_at = excluded.expires_at,
                metadata_json = excluded.metadata_json;
            """,
            [
                ("$id", item.Id.ToString("D")),
                ("$scope", item.Scope),
                ("$key", item.Key),
                ("$kind", item.Kind.ToString()),
                ("$content", item.Content),
                ("$sourceUri", item.Source?.Uri),
                ("$sourceKind", item.Source?.Kind),
                ("$sourceHash", item.Source?.ContentHash),
                ("$created", FormatTimestamp(item.CreatedAt)),
                ("$expires", item.ExpiresAt is null
                    ? null
                    : FormatTimestamp(item.ExpiresAt.Value)),
                ("$metadata", JsonSerializer.Serialize(item.Metadata))
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
            ORDER BY ci.created_at DESC, ci.id ASC
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
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(10)) ?? [];
        var tags = JsonSerializer.Deserialize<string[]>(reader.GetString(11)) ?? [];
        return new ContextItem
        {
            Id = Guid.Parse(reader.GetString(0)),
            Scope = reader.GetString(1),
            Key = reader.IsDBNull(2) ? null : reader.GetString(2),
            Kind = Enum.Parse<ContextKind>(reader.GetString(3)),
            Content = reader.GetString(4),
            Source = reader.IsDBNull(5)
                ? null
                : new ContextSource
                {
                    Uri = reader.GetString(5),
                    Kind = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ContentHash = reader.IsDBNull(7) ? null : reader.GetString(7)
                },
            CreatedAt = ParseTimestamp(reader.GetString(8)),
            ExpiresAt = reader.IsDBNull(9)
                ? null
                : ParseTimestamp(reader.GetString(9)),
            Metadata = metadata,
            Tags = tags
        };
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        string sql,
        IReadOnlyCollection<(string Name, object? Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        IReadOnlyCollection<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, sql, parameters);
        command.Transaction = transaction;
        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void AddKindFilter(
        IReadOnlyCollection<ContextKind>? kinds,
        ICollection<string> where,
        ICollection<(string Name, object? Value)> parameters)
    {
        if (kinds is null || kinds.Count == 0)
            return;
        var names = new List<string>();
        var index = 0;
        foreach (var kind in kinds.Distinct())
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kinds));
            var name = $"$kind{index++}";
            names.Add(name);
            parameters.Add((name, kind.ToString()));
        }
        where.Add($"ci.kind IN ({string.Join(", ", names)})");
    }

    private static void AddTagFilters(
        IReadOnlyCollection<string>? tags,
        ICollection<string> where,
        ICollection<(string Name, object? Value)> parameters)
    {
        if (tags is null)
            return;
        var normalized = NormalizeTags(tags);
        for (var index = 0; index < normalized.Count; index++)
        {
            var parameter = $"$tag{index}";
            where.Add($"EXISTS (SELECT 1 FROM context_tags ct{index} WHERE ct{index}.item_id = ci.id AND ct{index}.tag = {parameter})");
            parameters.Add((parameter, normalized[index]));
        }
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(tag => tag?.Trim().ToLowerInvariant())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToList();

    private static void ValidateItem(ContextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Content);
        if (!Enum.IsDefined(item.Kind))
            throw new ArgumentOutOfRangeException(nameof(item));
        if (item.Key is not null && string.IsNullOrWhiteSpace(item.Key))
            throw new ArgumentException("Logical key must not be blank.", nameof(item));
        if (item.Source is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Source.Uri);
        if (item.Metadata.Any(pair => pair.Key is null || pair.Value is null))
            throw new ArgumentException("Metadata keys and values must not be null.", nameof(item));
        if (item.Tags.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Tags must not be blank.", nameof(item));
    }

    private static void ValidateQuery(ContextQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (!Enum.IsDefined(query.SearchMode))
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.Scope is not null && string.IsNullOrWhiteSpace(query.Scope))
            throw new ArgumentException("Scope must not be blank.", nameof(query));
        if (query.Key is not null && string.IsNullOrWhiteSpace(query.Key))
            throw new ArgumentException("Key must not be blank.", nameof(query));
        if (query.SourceUri is not null && string.IsNullOrWhiteSpace(query.SourceUri))
            throw new ArgumentException("Source URI must not be blank.", nameof(query));
        if (query.Tags?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Tags must not be blank.", nameof(query));
    }

    private static void ValidateRelation(ContextRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        if (relation.FromId == Guid.Empty || relation.ToId == Guid.Empty)
            throw new ArgumentException("Relation IDs must not be empty.", nameof(relation));
        if (relation.FromId == relation.ToId)
            throw new ArgumentException("Self-relations are not supported.", nameof(relation));
        ArgumentException.ThrowIfNullOrWhiteSpace(relation.Kind);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private const string SelectColumns = """
        ci.id, ci.scope, ci.logical_key, ci.kind, ci.content,
        ci.source_uri, ci.source_kind, ci.source_hash,
        ci.created_at, ci.expires_at, ci.metadata_json,
        COALESCE((
            SELECT json_group_array(tag)
            FROM (SELECT tag FROM context_tags WHERE item_id = ci.id ORDER BY tag)
        ), '[]') AS tags_json
        """;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS cangjie_schema
        (
            version INTEGER NOT NULL
        );
        INSERT INTO cangjie_schema(version)
        SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM cangjie_schema);

        CREATE TABLE IF NOT EXISTS context_items
        (
            id TEXT PRIMARY KEY,
            scope TEXT NOT NULL,
            logical_key TEXT NULL,
            kind TEXT NOT NULL,
            content TEXT NOT NULL,
            source_uri TEXT NULL,
            source_kind TEXT NULL,
            source_hash TEXT NULL,
            created_at TEXT NOT NULL,
            expires_at TEXT NULL,
            metadata_json TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_context_items_scope
            ON context_items(scope);
        CREATE INDEX IF NOT EXISTS ix_context_items_scope_key
            ON context_items(scope, logical_key, created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_context_items_kind
            ON context_items(kind);
        CREATE INDEX IF NOT EXISTS ix_context_items_created
            ON context_items(created_at DESC, id ASC);
        CREATE INDEX IF NOT EXISTS ix_context_items_expires
            ON context_items(expires_at);

        CREATE TABLE IF NOT EXISTS context_tags
        (
            item_id TEXT NOT NULL,
            tag TEXT NOT NULL,
            PRIMARY KEY(item_id, tag),
            FOREIGN KEY(item_id) REFERENCES context_items(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_context_tags_tag
            ON context_tags(tag, item_id);

        CREATE TABLE IF NOT EXISTS context_relations
        (
            from_id TEXT NOT NULL,
            to_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            created_at TEXT NOT NULL,
            PRIMARY KEY(from_id, to_id, kind),
            FOREIGN KEY(from_id) REFERENCES context_items(id) ON DELETE CASCADE,
            FOREIGN KEY(to_id) REFERENCES context_items(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_context_relations_from
            ON context_relations(from_id, kind, to_id);
        CREATE INDEX IF NOT EXISTS ix_context_relations_to
            ON context_relations(to_id, kind, from_id);

        CREATE VIRTUAL TABLE IF NOT EXISTS context_items_fts
        USING fts5(item_id UNINDEXED, content, tokenize = 'unicode61');
        """;
}
