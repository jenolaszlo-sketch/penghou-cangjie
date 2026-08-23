namespace Penghou.Cangjie;

/// <summary>Contains a context item and its ordinal position in one result set.</summary>
public sealed record ContextSearchHit
{
    /// <summary>Gets the matched context item.</summary>
    public required ContextItem Item { get; init; }

    /// <summary>Gets the one-based rank within this result set.</summary>
    public required int Rank { get; init; }

    /// <summary>Gets the stable identifier of the retrieval strategy.</summary>
    public string Strategy { get; init; } = ContextSearchStrategies.Exact;

    /// <summary>Gets the implementation-defined version of the strategy.</summary>
    public string StrategyVersion { get; init; } = "1";

    /// <summary>
    /// Gets an optional strategy-local score. Scores are not comparable across
    /// strategies or versions.
    /// </summary>
    public double? Score { get; init; }
}
