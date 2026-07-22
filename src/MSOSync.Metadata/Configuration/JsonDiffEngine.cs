using System.Text.Json;
using MSOSync.Metadata.Configuration.Dtos;

namespace MSOSync.Metadata.Configuration;

internal static class JsonDiffEngine
{
    public static IReadOnlyList<DiffEntryDto> Diff(JsonElement v1Root, JsonElement v2Root)
    {
        var map1 = Flatten(v1Root, "");
        var map2 = Flatten(v2Root, "");

        var allKeys = new HashSet<string>(map1.Keys.Concat(map2.Keys));
        var entries = new List<DiffEntryDto>(allKeys.Count);

        foreach (var key in allKeys)
        {
            var has1 = map1.TryGetValue(key, out var val1);
            var has2 = map2.TryGetValue(key, out var val2);

            var changeType = (has1, has2) switch
            {
                (true, false) => "Removed",
                (false, true) => "Added",
                _             => val1 == val2 ? "Unchanged" : "Changed",
            };

            entries.Add(new DiffEntryDto(key, changeType, has1 ? val1 : null, has2 ? val2 : null));
        }

        return entries
            .OrderBy(e => e.ChangeType switch
            {
                "Changed"   => 0,
                "Added"     => 1,
                "Removed"   => 2,
                _           => 3,
            })
            .ThenBy(e => e.Key)
            .ToList()
            .AsReadOnly();
    }

    private static Dictionary<string, string> Flatten(JsonElement element, string prefix)
    {
        var result = new Dictionary<string, string>();
        FlattenInto(element, prefix, result);
        return result;
    }

    private static void FlattenInto(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                FlattenInto(prop.Value, key, result);
            }
        }
        else
        {
            // Arrays and scalars are treated as atomic string values
            result[prefix] = element.ValueKind == JsonValueKind.String
                ? element.GetString()!
                : element.GetRawText();
        }
    }
}
