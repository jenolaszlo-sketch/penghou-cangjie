namespace Penghou.Cangjie.Testing;

/// <summary>Describes the checks completed by a context-store conformance run.</summary>
public sealed record ContextStoreConformanceReport
{
    /// <summary>Gets the stable identifiers of completed checks.</summary>
    public required IReadOnlyList<string> CompletedChecks
    {
        get => completedChecks;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            completedChecks = Array.AsReadOnly(value.ToArray());
        }
    }

    private IReadOnlyList<string> completedChecks = Array.AsReadOnly(Array.Empty<string>());
}
