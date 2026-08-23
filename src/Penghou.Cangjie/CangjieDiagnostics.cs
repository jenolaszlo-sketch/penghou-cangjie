namespace Penghou.Cangjie;

using System.Diagnostics;

/// <summary>Exposes privacy-safe diagnostic activities emitted by Cangjie.</summary>
public static class CangjieDiagnostics
{
    /// <summary>Identifies the Cangjie diagnostic activity source.</summary>
    public const string ActivitySourceName = "Penghou.Cangjie";

    /// <summary>Gets the activity source used for store diagnostics.</summary>
    public static ActivitySource ActivitySource { get; } =
        new(ActivitySourceName);
}
