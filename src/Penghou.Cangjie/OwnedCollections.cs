using System.Collections.ObjectModel;

namespace Penghou.Cangjie;

internal static class OwnedCollections
{
    internal static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
    }

    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Array.AsReadOnly(value.ToArray());
    }
}
