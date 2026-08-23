using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Penghou.Cangjie.Sqlite;

/// <summary>
/// Owns one SQLite context database: the connection string, the initialize-once
/// gate, PRAGMA configuration, schema bootstrap, and version verification.
/// </summary>
internal sealed class CangjieDatabase
{
    private const int CurrentVersion = 4;

    private readonly string connectionString;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool initialized;

    public CangjieDatabase(CangjieSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);
        if (options.BusyTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));

        Options = options;
        var path = Path.GetFullPath(options.DatabasePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = options.Pooling
        }.ToString();
    }

    public CangjieSqliteOptions Options { get; }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
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
            if (Options.EnableWal)
                await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken)
                    .ConfigureAwait(false);
            await ExecuteAsync(connection, SchemaBootstrap, cancellationToken)
                .ConfigureAwait(false);
            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT MAX(version) FROM cangjie_schema;";
            var version = Convert.ToInt32(
                await versionCommand.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (version != CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"Cangjie database schema version {version} is not supported by this preview. Recreate the database with schema version {CurrentVersion}.");
            }
            await ExecuteAsync(connection, Schema, cancellationToken)
                .ConfigureAwait(false);
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    public async ValueTask EnsureInitializedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!initialized)
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SqliteConnection> OpenAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection,
            $"PRAGMA busy_timeout = {(long)Options.BusyTimeout.TotalMilliseconds};",
            cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaBootstrap = """
        CREATE TABLE IF NOT EXISTS cangjie_schema
        (
            version INTEGER NOT NULL
        );
        INSERT INTO cangjie_schema(version)
        SELECT 4 WHERE NOT EXISTS (SELECT 1 FROM cangjie_schema);
        """;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS context_items
        (
            id TEXT PRIMARY KEY,
            scope TEXT NOT NULL,
            logical_key TEXT NULL,
            revision INTEGER NOT NULL,
            kind TEXT NOT NULL,
            content TEXT NOT NULL,
            source_uri TEXT NULL,
            source_kind TEXT NULL,
            source_hash TEXT NULL,
            producer TEXT NOT NULL,
            producer_version TEXT NULL,
            originated_at TEXT NULL,
            provenance_attributes_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            expires_at TEXT NULL,
            metadata_json TEXT NOT NULL,
            idempotency_key TEXT NULL,
            request_hash TEXT NOT NULL,
            CHECK (length(scope) > 0),
            CHECK (length(kind) > 0),
            CHECK (length(content) > 0),
            CHECK (revision >= 1),
            CHECK (request_hash <> '')
        );
        CREATE INDEX IF NOT EXISTS ix_context_items_scope
            ON context_items(scope);
        CREATE INDEX IF NOT EXISTS ix_context_items_scope_key
            ON context_items(scope, logical_key, revision DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_context_items_scope_key_revision
            ON context_items(scope, logical_key, revision)
            WHERE logical_key IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_context_items_scope_idempotency
            ON context_items(scope, idempotency_key)
            WHERE idempotency_key IS NOT NULL;
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
            CHECK (from_id <> to_id),
            CHECK (length(kind) > 0),
            FOREIGN KEY(from_id) REFERENCES context_items(id) ON DELETE CASCADE,
            FOREIGN KEY(to_id) REFERENCES context_items(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_context_relations_from
            ON context_relations(from_id, kind, to_id);
        CREATE INDEX IF NOT EXISTS ix_context_relations_to
            ON context_relations(to_id, kind, from_id);

        CREATE TABLE IF NOT EXISTS context_snapshots
        (
            id TEXT PRIMARY KEY,
            query_identity TEXT NOT NULL,
            strategy TEXT NOT NULL,
            strategy_version TEXT NOT NULL,
            selected_at TEXT NOT NULL,
            purpose TEXT NULL,
            metadata_json TEXT NOT NULL,
            CHECK (length(query_identity) > 0),
            CHECK (length(strategy) > 0)
        );

        CREATE TABLE IF NOT EXISTS context_snapshot_items
        (
            snapshot_id TEXT NOT NULL,
            item_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            PRIMARY KEY(snapshot_id, ordinal),
            UNIQUE(snapshot_id, item_id),
            FOREIGN KEY(snapshot_id) REFERENCES context_snapshots(id) ON DELETE RESTRICT,
            FOREIGN KEY(item_id) REFERENCES context_items(id) ON DELETE RESTRICT
        );
        CREATE INDEX IF NOT EXISTS ix_context_snapshot_items_item
            ON context_snapshot_items(item_id, snapshot_id);

        CREATE VIRTUAL TABLE IF NOT EXISTS context_items_fts
        USING fts5(item_id UNINDEXED, content, tokenize = 'unicode61');
        """;
}
