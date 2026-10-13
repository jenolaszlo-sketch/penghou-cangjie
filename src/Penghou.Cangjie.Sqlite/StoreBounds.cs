using System.Text;
using System.Text.Json;

namespace Penghou.Cangjie.Sqlite;

internal static class StoreBounds
{
    internal const int Collection = 1000;
    internal const int Batch = 100;
    internal const int ContentBytes = 1024 * 1024;
    internal const int MetadataBytes = 64 * 1024;
    internal const int StringLength = 4096;

    internal static void Text(string? value, string name)
    {
        if (value?.Length > StringLength)
            throw new ArgumentOutOfRangeException(name, $"Text must not exceed {StringLength} UTF-16 code units.");
    }

    internal static void Strings(IEnumerable<string>? values, string name)
    {
        if (values is null) return;
        var count = 0;
        foreach (var value in values)
        {
            if (++count > 100) throw new ArgumentOutOfRangeException(name, "At most 100 values are allowed.");
            Text(value, name);
        }
    }

    internal static void Metadata(IReadOnlyDictionary<string, string> value, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Count > 100 || Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value)) > MetadataBytes)
            throw new ArgumentOutOfRangeException(name, "Metadata must have at most 100 entries and 65536 UTF-8 JSON bytes.");
        foreach (var pair in value)
        {
            Text(pair.Key, name);
            Text(pair.Value, name);
        }
    }
}
