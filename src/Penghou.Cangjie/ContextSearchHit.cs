namespace Penghou.Cangjie;

/// <summary>Contains a context item and its ordinal position in one result set.</summary>
public sealed record ContextSearchHit
{
    /// <summary>Gets the matched context item.</summary>
    public required ContextItem Item { get; init; }

    /// <summary>Gets the one-based rank within this result set.</summary>
    public required int Rank { get; init; }
}
