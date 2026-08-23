using Microsoft.Data.Sqlite;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Penghou.Cangjie.Sqlite;

/// <summary>Shared command helpers, validation, and canonical hashing.</summary>
public sealed partial class SqliteContextStore
{
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

    private const string SelectColumns = """
        ci.id, ci.scope, ci.logical_key, ci.revision, ci.kind, ci.content,
        ci.source_uri, ci.source_kind, ci.source_hash,
        ci.producer, ci.producer_version, ci.originated_at,
        ci.provenance_attributes_json,
        ci.created_at, ci.expires_at, ci.metadata_json,
        COALESCE((
            SELECT json_group_array(tag)
            FROM (SELECT tag FROM context_tags WHERE item_id = ci.id ORDER BY tag)
        ), '[]') AS tags_json
        """;

    private static void ValidateItem(ContextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Kind);
        if (item.Revision != 0)
            throw new ArgumentException("Revision is assigned by the context store.", nameof(item));
        if (item.Key is not null && string.IsNullOrWhiteSpace(item.Key))
            throw new ArgumentException("Logical key must not be blank.", nameof(item));
        ArgumentNullException.ThrowIfNull(item.Provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Provenance.Producer);
        if (item.Provenance.Source is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Provenance.Source.Uri);
        if (item.Provenance.Attributes.Any(
            pair => pair.Key is null || pair.Value is null))
        {
            throw new ArgumentException(
                "Provenance attribute keys and values must not be null.",
                nameof(item));
        }
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
        if (query.Scope is not null && query.Scopes is not null)
            throw new ArgumentException("Scope and Scopes cannot both be supplied.", nameof(query));
        if (query.Scopes is { Count: 0 or > 100 })
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.Scopes?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Scopes must not contain blank values.", nameof(query));
        if (query.Scopes is not null &&
            query.Scopes.Distinct(StringComparer.Ordinal).Count() != query.Scopes.Count)
        {
            throw new ArgumentException("Scopes must not contain duplicates.", nameof(query));
        }
        if (query.Key is not null && string.IsNullOrWhiteSpace(query.Key))
            throw new ArgumentException("Key must not be blank.", nameof(query));
        if (query.SourceUri is not null && string.IsNullOrWhiteSpace(query.SourceUri))
            throw new ArgumentException("Source URI must not be blank.", nameof(query));
        if (query.Tags?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Tags must not be blank.", nameof(query));
        if (query.Kinds?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Kinds must not be blank.", nameof(query));
    }

    private static void ValidateWriteOptions(
        ContextItem item,
        ContextWriteOptions? options)
    {
        if (options?.ExpectedRevision is < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options?.ExpectedRevision is not null && item.Key is null)
        {
            throw new ArgumentException(
                "Expected revision requires a logical key.",
                nameof(options));
        }
        if (options?.IdempotencyKey is not null &&
            string.IsNullOrWhiteSpace(options.IdempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key must not be blank.",
                nameof(options));
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
