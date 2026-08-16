namespace Penghou.Cangjie;

/// <summary>Classifies the purpose of explicitly stored context.</summary>
public enum ContextKind
{
    /// <summary>Raw or near-raw information observed by a system.</summary>
    Evidence,
    /// <summary>Reusable information accepted within a scope.</summary>
    Knowledge,
    /// <summary>A choice made by a workflow or application.</summary>
    Decision,
    /// <summary>A compact representation derived from other context.</summary>
    Summary,
    /// <summary>A reference or description of something produced by a process.</summary>
    Artifact
}
