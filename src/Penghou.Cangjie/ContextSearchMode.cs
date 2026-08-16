namespace Penghou.Cangjie;

/// <summary>Controls how normalized lexical terms are combined.</summary>
public enum ContextSearchMode
{
    /// <summary>Requires every normalized term.</summary>
    AllTerms,
    /// <summary>Requires at least one normalized term.</summary>
    AnyTerm,
    /// <summary>Requires normalized terms as one phrase in their supplied order.</summary>
    Phrase
}
