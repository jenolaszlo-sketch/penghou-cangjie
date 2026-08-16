using System.Text.RegularExpressions;

namespace Penghou.Cangjie.Sqlite;

internal static partial class SafeFtsQueryBuilder
{
    public static string? Build(string? text, ContextSearchMode mode)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var terms = TermPattern().Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length == 0)
            return null;

        return mode switch
        {
            ContextSearchMode.AllTerms => string.Join(
                " AND ",
                terms.Select(Quote)),
            ContextSearchMode.AnyTerm => string.Join(
                " OR ",
                terms.Select(Quote)),
            ContextSearchMode.Phrase => Quote(string.Join(' ', terms)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported context search mode.")
        };
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    [GeneratedRegex(@"[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)]
    private static partial Regex TermPattern();
}
