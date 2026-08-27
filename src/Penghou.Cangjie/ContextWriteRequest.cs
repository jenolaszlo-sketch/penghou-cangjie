namespace Penghou.Cangjie;

/// <summary>Describes one item append within an ordered atomic batch.</summary>
public sealed record ContextWriteRequest
{
    /// <summary>Gets the immutable context revision to append.</summary>
    public required ContextItem Item { get; init; }

    /// <summary>Gets optional concurrency and idempotency controls for the append.</summary>
    public ContextWriteOptions? Options { get; init; }
}
