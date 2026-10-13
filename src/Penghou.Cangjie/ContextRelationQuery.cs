namespace Penghou.Cangjie;

/// <summary>Describes bounded filtering of relations around one context item.</summary>
public sealed record ContextRelationQuery
{
    /// <summary>Gets which ends of directed relations are included.</summary>
    public ContextRelationDirection Direction { get; init; } =
        ContextRelationDirection.Outgoing;

    /// <summary>Gets optional exact relation kinds to include.</summary>
    public IReadOnlyCollection<string>? Kinds { get => kinds; init => kinds = value is null ? null : OwnedCollections.Copy<string>(value); }

    private IReadOnlyCollection<string>? kinds;

    /// <summary>Gets the maximum result count, from 1 through 100.</summary>
    public int Limit { get; init; } = 100;
}
