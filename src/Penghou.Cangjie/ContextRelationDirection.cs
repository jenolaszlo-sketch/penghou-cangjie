namespace Penghou.Cangjie;

/// <summary>Selects which ends of directed relationships are retrieved.</summary>
public enum ContextRelationDirection
{
    /// <summary>Returns relationships whose source is the requested item.</summary>
    Outgoing,
    /// <summary>Returns relationships whose target is the requested item.</summary>
    Incoming,
    /// <summary>Returns relationships involving the requested item at either end.</summary>
    Both
}
