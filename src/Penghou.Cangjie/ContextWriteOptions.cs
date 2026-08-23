namespace Penghou.Cangjie;

/// <summary>Controls concurrency and retry identity for one immutable append.</summary>
public sealed record ContextWriteOptions
{
    /// <summary>
    /// Gets the revision the caller expects to be current, or null to append
    /// without an optimistic-concurrency precondition. Use zero when expecting
    /// that no revision exists.
    /// </summary>
    public int? ExpectedRevision { get; init; }

    /// <summary>
    /// Gets an optional caller-defined retry identity, unique within the exact
    /// context scope.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
