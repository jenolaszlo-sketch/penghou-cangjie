namespace Penghou.Cangjie;

/// <summary>Contains stable identifiers for built-in retrieval strategies.</summary>
public static class ContextSearchStrategies
{
    /// <summary>Identifies indexed-filter retrieval without lexical matching.</summary>
    public const string Exact = "exact";

    /// <summary>Identifies lexical full-text retrieval.</summary>
    public const string Lexical = "lexical";
}
