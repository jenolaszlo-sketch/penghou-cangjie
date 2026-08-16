namespace Penghou.Cangjie;

/// <summary>Identifies the origin of a context item without dereferencing it.</summary>
public sealed record ContextSource
{
    /// <summary>Gets the opaque, consumer-owned provenance URI.</summary>
    public required string Uri { get; init; }

    /// <summary>Gets an optional consumer-defined source classification.</summary>
    public string? Kind { get; init; }

    /// <summary>Gets an optional hash of the source content.</summary>
    public string? ContentHash { get; init; }
}
