namespace Penghou.Cangjie;

/// <summary>Indicates that an immutable append conflicts with existing durable state.</summary>
public sealed class ContextStoreConflictException : InvalidOperationException
{
    /// <summary>Initializes a conflict with a descriptive message.</summary>
    public ContextStoreConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a conflict caused by an underlying store failure.</summary>
    public ContextStoreConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
