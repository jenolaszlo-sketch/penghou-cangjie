namespace Penghou.Cangjie;

/// <summary>Identifies where context originated and what recorded or derived it.</summary>
public sealed record ContextProvenance
{
    /// <summary>Gets the optional external source from which the context originated.</summary>
    public ContextSource? Source { get; init; }

    /// <summary>Gets the stable caller-defined identifier of the producer.</summary>
    public required string Producer { get; init; }

    /// <summary>Gets the optional producer implementation or model version.</summary>
    public string? ProducerVersion { get; init; }

    /// <summary>Gets when the information originated, if known.</summary>
    public DateTimeOffset? OriginatedAt { get; init; }

    /// <summary>Gets additional provider-neutral provenance attributes.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>();
}
