namespace Penghou.Cangjie;

/// <summary>Provides stable identifiers for common context classifications.</summary>
public static class ContextKinds
{
    /// <summary>Raw or near-raw information observed by a system.</summary>
    public const string Evidence = "evidence";
    /// <summary>Reusable information accepted within a scope.</summary>
    public const string Knowledge = "knowledge";
    /// <summary>A choice made by a workflow or application.</summary>
    public const string Decision = "decision";
    /// <summary>A compact representation derived from other context.</summary>
    public const string Summary = "summary";
    /// <summary>A reference or description of something produced by a process.</summary>
    public const string Artifact = "artifact";
}
