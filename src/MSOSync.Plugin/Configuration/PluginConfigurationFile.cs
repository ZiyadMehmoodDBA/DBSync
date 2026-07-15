using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MSOSync.Plugin.Configuration;

internal sealed class PluginConfigurationFile
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    private PluginConfigurationFile(Dictionary<string, string?> values)
        => _values = values;

    public static PluginConfigurationFile Empty { get; } = new([]);

    public string? GetValue(string key)
        => _values.TryGetValue(key, out var v) ? v : null;

    public IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)_values.Keys;

    internal static PluginConfigurationFile FromValues(Dictionary<string, string?> values)
        => new(values);

    public static PluginConfigurationFile Load(string configPath, ILogger logger, long maxSizeBytes)
    {
        if (!File.Exists(configPath))
            return Empty;

        var info = new FileInfo(configPath);
        if (info.Length > maxSizeBytes)
        {
            logger.LogWarning(
                "plugin.config.json at {Path} is {Size} bytes which exceeds the {Max} byte limit; ignoring",
                configPath, info.Length, maxSizeBytes);
            return Empty;
        }

        try
        {
            var json    = File.ReadAllText(configPath);
            var doc     = JsonDocument.Parse(json);
            var dict    = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            FlattenElement(string.Empty, doc.RootElement, dict);
            return new PluginConfigurationFile(dict);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to parse plugin.config.json at {Path}; plugin will use appsettings only",
                configPath);
            return Empty;
        }
    }

    private static void FlattenElement(string prefix, JsonElement element, Dictionary<string, string?> dict)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? prop.Name
                        : $"{prefix}:{prop.Name}";
                    FlattenElement(key, prop.Value, dict);
                }
                break;

            case JsonValueKind.Null:
                dict[prefix] = null;
                break;

            default:
                // Strings, numbers, booleans — strip outer quotes from strings
                dict[prefix] = element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.GetRawText();
                break;
        }
    }
}
