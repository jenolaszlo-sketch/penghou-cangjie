namespace Penghou.Cangjie;

/// <summary>Represents one directed relationship between context records.</summary>
public sealed record ContextRelation
{
    /// <summary>Gets the identity at the relationship's outgoing end.</summary>
    public required Guid FromId { get; init; }

    /// <summary>Gets the identity at the relationship's incoming end.</summary>
    public required Guid ToId { get; init; }

    /// <summary>Gets the stable built-in or consumer-defined relation identifier.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets when the relationship was recorded.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
