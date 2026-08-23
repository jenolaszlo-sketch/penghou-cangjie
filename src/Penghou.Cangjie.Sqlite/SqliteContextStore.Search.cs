using System.Diagnostics;

namespace Penghou.Cangjie.Sqlite;

/// <summary>Deterministic lexical and indexed search over stored context.</summary>
public sealed partial class SqliteContextStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextSearchHit>> SearchAsync(
        ContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        var started = Stopwatch.GetTimestamp();
        var strategy = string.IsNullOrWhiteSpace(query.Text)
            ? ContextSearchStrategies.Exact
            : ContextSearchStrategies.Lexical;
        using var activity = CangjieDiagnostics.ActivitySource.StartActivity(
            "context.search",
            ActivityKind.Internal);
        SetSearchRequestDiagnostics(activity, query, strategy);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        var ftsQuery = SafeFtsQueryBuilder.Build(query.Text, query.SearchMode);
        if (!string.IsNullOrWhiteSpace(query.Text) && ftsQuery is null)
        {
            activity?.SetTag("cangjie.search.result_count", 0);
            return [];
        }

        var parameters = new List<(string Name, object? Value)>();
        var where = new List<string>();
        string? scopePriority = null;
        if (query.Scope is not null)
        {
            where.Add("ci.scope = $scope");
            parameters.Add(("$scope", query.Scope));
        }
        else if (query.Scopes is { Count: > 0 })
        {
            var scopeParameters = new List<string>(query.Scopes.Count);
            var scopeCases = new List<string>(query.Scopes.Count);
            for (var index = 0; index < query.Scopes.Count; index++)
            {
                var name = $"$scope{index}";
                scopeParameters.Add(name);
                scopeCases.Add($"WHEN {name} THEN {index}");
                parameters.Add((name, query.Scopes[index]));
            }

            where.Add($"ci.scope IN ({string.Join(", ", scopeParameters)})");
            scopePriority = $"CASE ci.scope {string.Join(" ", scopeCases)} END";
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
        var sql = scopePriority is null
            ? $"""
                SELECT {SelectColumns}
                FROM context_items ci
                {join}
                WHERE {(where.Count == 0 ? "1 = 1" : string.Join(" AND ", where))}
                ORDER BY {order}
                LIMIT $limit;
                """
            : BuildLayeredScopeQuery(
                join,
                where,
                scopePriority,
                ftsQuery is not null);
        await using var command = CreateCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<ContextSearchHit>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ContextSearchHit
            {
                Item = ReadItem(reader),
                Rank = results.Count + 1,
                Strategy = strategy,
                StrategyVersion = "sqlite-v1"
            });
        }
        activity?.SetTag("cangjie.search.result_count", results.Count);
        CangjieDiagnostics.SearchDuration.Record(
            Stopwatch.GetElapsedTime(started).TotalSeconds);
        return results;
    }

    private static void SetSearchRequestDiagnostics(
        Activity? activity,
        ContextQuery query,
        string strategy)
    {
        if (activity is null)
            return;

        activity.SetTag("cangjie.search.strategy", strategy);
        activity.SetTag("cangjie.search.has_text", query.Text is not null);
        activity.SetTag(
            "cangjie.search.scope_count",
            query.Scopes?.Count ?? (query.Scope is null ? 0 : 1));
        activity.SetTag("cangjie.search.kind_count", query.Kinds?.Count ?? 0);
        activity.SetTag("cangjie.search.tag_count", query.Tags?.Count ?? 0);
        activity.SetTag("cangjie.search.limit", query.Limit);
        activity.SetTag("cangjie.search.include_expired", query.IncludeExpired);
    }

    private static void AddKindFilter(
        IReadOnlyCollection<string>? kinds,
        ICollection<string> where,
        ICollection<(string Name, object? Value)> parameters)
    {
        if (kinds is null || kinds.Count == 0)
            return;
        var names = new List<string>();
        var index = 0;
        foreach (var kind in kinds.Select(value => value?.Trim())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal))
        {
            var name = $"$kind{index++}";
            names.Add(name);
            parameters.Add((name, kind));
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

    private static string BuildLayeredScopeQuery(
        string join,
        IReadOnlyCollection<string> where,
        string scopePriority,
        bool hasFtsQuery)
    {
        var relevance = hasFtsQuery ? "bm25(context_items_fts)" : "0.0";
        return $"""
            WITH candidates AS (
                SELECT {SelectColumns},
                       {scopePriority} AS scope_priority,
                       {relevance} AS search_relevance,
                       CASE
                           WHEN ci.logical_key IS NULL THEN 'id:' || ci.id
                           ELSE 'key:' || ci.logical_key
                       END AS concept_identity
                FROM context_items ci
                {join}
                WHERE {string.Join(" AND ", where)}
            ), ranked AS (
                SELECT *,
                       ROW_NUMBER() OVER (
                           PARTITION BY concept_identity
                           ORDER BY scope_priority ASC, search_relevance ASC,
                                    created_at DESC, id ASC
                       ) AS concept_rank
                FROM candidates
            )
            SELECT *
            FROM ranked
            WHERE concept_rank = 1
            ORDER BY scope_priority ASC, search_relevance ASC,
                     created_at DESC, id ASC
            LIMIT $limit;
            """;
    }
}
