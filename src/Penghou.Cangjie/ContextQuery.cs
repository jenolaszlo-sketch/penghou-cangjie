namespace Penghou.Cangjie;

/// <summary>Describes lexical retrieval and exact indexed filters.</summary>
public sealed record ContextQuery
{
    /// <summary>Gets optional user text to normalize into a safe lexical query.</summary>
    public string? Text { get; init; }

    /// <summary>Gets an optional exact scope filter.</summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Gets optional exact scopes in descending precedence. Keyed logical
    /// concepts are returned once from their highest-precedence scope.
    /// </summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>Gets an optional exact logical-key filter.</summary>
    public string? Key { get; init; }

    /// <summary>Gets an optional exact provenance URI filter.</summary>
    public string? SourceUri { get; init; }

    /// <summary>Gets optional allowed context kinds.</summary>
    public IReadOnlyCollection<string>? Kinds { get; init; }

    /// <summary>Gets tags that must all be present.</summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>Gets the maximum result count, from 1 through 100.</summary>
    public int Limit { get; init; } = 10;

    /// <summary>Gets whether expired items participate in search.</summary>
    public bool IncludeExpired { get; init; }

    /// <summary>Gets how normalized text terms are combined.</summary>
    public ContextSearchMode SearchMode { get; init; } =
        ContextSearchMode.AllTerms;
}
