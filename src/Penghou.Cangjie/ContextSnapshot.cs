namespace Penghou.Cangjie;

/// <summary>Records an immutable, ordered selection of physical context items.</summary>
public sealed record ContextSnapshot
{
    /// <summary>Gets the stable snapshot identity.</summary>
    public Guid Id { get; init; }
    /// <summary>Gets exact physical item identities in consumer-visible order.</summary>
    public required IReadOnlyList<Guid> ItemIds { get; init; }
    /// <summary>Gets the caller-defined query or specification identity.</summary>
    public required string QueryIdentity { get; init; }
    /// <summary>Gets the retrieval strategy that produced the selection.</summary>
    public required string Strategy { get; init; }
    /// <summary>Gets the retrieval strategy version.</summary>
    public required string StrategyVersion { get; init; }
    /// <summary>Gets when the selection was persisted.</summary>
    public DateTimeOffset SelectedAt { get; init; }
    /// <summary>Gets an optional caller-defined purpose.</summary>
    public string? Purpose { get; init; }
    /// <summary>Gets optional provider-neutral selection metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}
