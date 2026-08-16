namespace Penghou.Cangjie;

/// <summary>Provides stable identifiers for common provenance relationships.</summary>
public static class ContextRelationKinds
{
    /// <summary>Indicates that the source was derived from the target.</summary>
    public const string DerivedFrom = "derived-from";
    /// <summary>Indicates that the source supports the target.</summary>
    public const string Supports = "supports";
    /// <summary>Indicates that the source contradicts the target.</summary>
    public const string Contradicts = "contradicts";
    /// <summary>Indicates that the source was produced by the target.</summary>
    public const string ProducedBy = "produced-by";
    /// <summary>Indicates that the source is a newer revision of the target.</summary>
    public const string Supersedes = "supersedes";
    /// <summary>Indicates a general explicit reference from source to target.</summary>
    public const string References = "references";
}
