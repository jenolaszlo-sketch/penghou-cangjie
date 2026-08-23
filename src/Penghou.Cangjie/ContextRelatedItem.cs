namespace Penghou.Cangjie;

/// <summary>Pairs a relation with the context item at its opposite end.</summary>
public sealed record ContextRelatedItem
{
    /// <summary>Gets the relation connecting the requested and related items.</summary>
    public required ContextRelation Relation { get; init; }

    /// <summary>Gets the context item at the relation's opposite end.</summary>
    public required ContextItem Item { get; init; }
}
