using Microsoft.Data.Sqlite;

namespace Penghou.Cangjie.Sqlite;

/// <summary>Directed relations between context items.</summary>
public sealed partial class SqliteContextStore
{
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
        await InsertRelationAsync(
            connection,
            null,
            relation with { CreatedAt = createdAt },
            cancellationToken).ConfigureAwait(false);
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
        return await ReadRelationsAsync(
            connection,
            id,
            direction,
            null,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextRelation>> QueryRelationsAsync(
        Guid id,
        ContextRelationQuery query,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Context ID must not be empty.", nameof(id));
        ValidateRelationQuery(query);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ReadRelationsAsync(
            connection,
            id,
            query.Direction,
            query.Kinds,
            query.Limit,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ContextRelation>> ReadRelationsAsync(
        SqliteConnection connection,
        Guid id,
        ContextRelationDirection direction,
        IReadOnlyCollection<string>? kinds,
        int? limit,
        CancellationToken cancellationToken)
    {
        var predicate = direction switch
        {
            ContextRelationDirection.Outgoing => "(from_id = $id)",
            ContextRelationDirection.Incoming => "(to_id = $id)",
            ContextRelationDirection.Both => "(from_id = $id OR to_id = $id)",
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        var parameters = new List<(string Name, object? Value)>
        {
            ("$id", id.ToString("D"))
        };
        var normalizedKinds = kinds?
            .Select(kind => kind.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToArray();
        if (normalizedKinds is { Length: > 0 })
        {
            var kindParameters = new List<string>(normalizedKinds.Length);
            for (var index = 0; index < normalizedKinds.Length; index++)
            {
                var name = $"$relationKind{index}";
                kindParameters.Add(name);
                parameters.Add((name, normalizedKinds[index]));
            }
            predicate += $" AND kind IN ({string.Join(", ", kindParameters)})";
        }
        if (limit is not null)
            parameters.Add(("$limit", limit.Value));
        await using var command = CreateCommand(connection, $"""
            SELECT from_id, to_id, kind, created_at
            FROM context_relations
            WHERE {predicate}
            ORDER BY created_at ASC, from_id ASC, to_id ASC, kind ASC
            {(limit is null ? string.Empty : "LIMIT $limit")};
            """, parameters);
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

    private static Task<int> InsertRelationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ContextRelation relation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, transaction,
            """
            INSERT OR IGNORE INTO context_relations
                (from_id, to_id, kind, created_at)
            VALUES ($from, $to, $kind, $created);
            """,
            [
                ("$from", relation.FromId.ToString("D")),
                ("$to", relation.ToId.ToString("D")),
                ("$kind", relation.Kind.Trim()),
                ("$created", FormatTimestamp(relation.CreatedAt))
            ], cancellationToken);

    private static void ValidateRelation(ContextRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        if (relation.FromId == Guid.Empty || relation.ToId == Guid.Empty)
            throw new ArgumentException("Relation IDs must not be empty.", nameof(relation));
        if (relation.FromId == relation.ToId)
            throw new ArgumentException("Self-relations are not supported.", nameof(relation));
        ArgumentException.ThrowIfNullOrWhiteSpace(relation.Kind);
    }

    private static void ValidateRelationQuery(ContextRelationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(query.Direction))
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.Limit is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.Kinds?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Relation kinds must not be blank.", nameof(query));
    }
}
