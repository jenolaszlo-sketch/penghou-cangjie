namespace Penghou.Cangjie;

/// <summary>Contains a snapshot and its exact items in recorded order.</summary>
public sealed record ContextSnapshotResolution
{
    /// <summary>Gets the immutable snapshot.</summary>
    public required ContextSnapshot Snapshot { get; init; }
    /// <summary>Gets pinned physical items in snapshot order.</summary>
    public required IReadOnlyList<ContextItem> Items { get => items; init => items = OwnedCollections.Copy<ContextItem>(value); }

    private IReadOnlyList<ContextItem> items = Array.AsReadOnly(Array.Empty<ContextItem>());
}
