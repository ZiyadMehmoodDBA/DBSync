using Microsoft.Extensions.Configuration;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Configuration;

internal sealed class PluginConfigurationAdapter(
    IConfiguration          appSection,
    PluginConfigurationFile file) : IPluginConfiguration
{
    public T? GetValue<T>(string key)
    {
        // Priority 1: appsettings section — IConfiguration handles type conversion
        var appStr = appSection[key];
        if (appStr is not null)
            return appSection.GetValue<T>(key);

        // Priority 2: plugin.config.json file
        var fileStr = file.GetValue(key);
        if (fileStr is null)
            return default;

        try
        {
            var underlying = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T?)Convert.ChangeType(fileStr, underlying);
        }
        catch
        {
            return default;
        }
    }

    public T GetValue<T>(string key, T defaultValue)
    {
        var value = GetValue<T>(key);

        // Handle nullable types: if GetValue<T> returns null, use default
        if (value is null)
            return defaultValue;

        // Handle value types: check if the value equals default(T)
        // This requires checking if both the key exists and conversion was successful
        if (typeof(T).IsValueType && typeof(T) != typeof(string))
        {
            // For value types, we need to distinguish between "not found" and "default value"
            // Only return default value if the key truly doesn't exist in either source
            var appStr = appSection[key];
            var fileStr = file.GetValue(key);

            if (appStr is null && fileStr is null)
                return defaultValue;
        }

        return value!;
    }

    public IPluginConfiguration GetSection(string sectionName)
    {
        var prefix    = sectionName + ":";
        var subValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var k in file.Keys)
        {
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                subValues[k[prefix.Length..]] = file.GetValue(k);
        }

        return new PluginConfigurationAdapter(
            appSection.GetSection(sectionName),
            PluginConfigurationFile.FromValues(subValues));
    }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in appSection.GetChildren())
                keys.Add(child.Key);
            foreach (var k in file.Keys)
                keys.Add(k);
            return keys;
        }
    }

    public bool Exists(string key)
        => appSection[key] is not null || file.GetValue(key) is not null;
}
