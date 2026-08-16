namespace Penghou.Cangjie;

/// <summary>Represents one explicit, attributable context record.</summary>
public sealed record ContextItem
{
    /// <summary>Gets the physical identity of this recorded revision.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the exact consumer-defined isolation scope.</summary>
    public required string Scope { get; init; }

    /// <summary>Gets an optional logical identity shared by successive revisions.</summary>
    public string? Key { get; init; }

    /// <summary>Gets the purpose of the stored context.</summary>
    public required ContextKind Kind { get; init; }

    /// <summary>Gets the searchable textual content.</summary>
    public required string Content { get; init; }

    /// <summary>Gets the optional provenance source.</summary>
    public ContextSource? Source { get; init; }

    /// <summary>Gets when this revision was first stored.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when this item becomes hidden from ordinary searches.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets consumer-defined string metadata not included in full-text search.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Gets normalized labels used for indexed filtering.</summary>
    public IReadOnlyCollection<string> Tags { get; init; } = [];
}
