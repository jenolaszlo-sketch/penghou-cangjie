namespace Penghou.Cangjie;

/// <summary>Controls how normalized lexical terms are combined.</summary>
public enum ContextSearchMode
{
    /// <summary>Requires every normalized term.</summary>
    AllTerms,
    /// <summary>Requires at least one normalized term.</summary>
    AnyTerm,
    /// <summary>
    /// Requires one exact contiguous phrase in supplied term order. Use
    /// <see cref="AllTerms"/> when terms may appear separately.
    /// </summary>
    Phrase
}
