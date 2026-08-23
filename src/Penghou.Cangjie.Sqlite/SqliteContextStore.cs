using Microsoft.Data.Sqlite;

namespace Penghou.Cangjie.Sqlite;

/// <summary>
/// Implements transactional local context storage and safe FTS5 retrieval.
/// The public surface is split across partial files by capability; schema,
/// initialization, and connection ownership live in <see cref="CangjieDatabase"/>.
/// </summary>
public sealed partial class SqliteContextStore : IContextStore
{
    private readonly CangjieSqliteOptions options;
    private readonly TimeProvider timeProvider;
    private readonly CangjieDatabase database;

    /// <summary>Initializes a store for a SQLite database file.</summary>
    /// <param name="options">SQLite persistence options.</param>
    /// <param name="timeProvider">Optional clock used for timestamps and expiration.</param>
    public SqliteContextStore(
        CangjieSqliteOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BusyTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));

        this.options = options;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        database = new CangjieDatabase(options);
    }

    /// <inheritdoc />
    public async ValueTask<ContextStoreHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await database.EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var connection = await database.OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return new ContextStoreHealth
            {
                IsHealthy = true,
                StoreName = "sqlite",
                SchemaVersion = 4,
                WalMode = options.EnableWal
            };
        }
        catch (Exception exception)
        {
            return new ContextStoreHealth
            {
                IsHealthy = false,
                StoreName = "sqlite",
                Detail = exception.Message
            };
        }
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken) =>
        await database.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

    private ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
        database.OpenAsync(cancellationToken);
}
