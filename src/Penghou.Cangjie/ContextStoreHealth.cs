namespace Penghou.Cangjie;

/// <summary>Result of a store health probe used by readiness endpoints.</summary>
public sealed record ContextStoreHealth
{
    /// <summary>Gets whether the backing store can serve context operations.</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>Gets the store implementation name.</summary>
    public string? StoreName { get; init; }

    /// <summary>Gets the detected schema version, when known.</summary>
    public int? SchemaVersion { get; init; }

    /// <summary>Gets whether write-ahead logging is enabled, when known.</summary>
    public bool? WalMode { get; init; }

    /// <summary>Gets failure detail when the probe was unsuccessful.</summary>
    public string? Detail { get; init; }
}
