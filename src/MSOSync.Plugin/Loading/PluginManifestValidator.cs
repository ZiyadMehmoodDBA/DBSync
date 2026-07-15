namespace MSOSync.Plugin.Loading;

public static class PluginManifestValidator
{
    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <summary>
    /// Validates a parsed manifest. Returns null on success, or an error message on failure.
    /// </summary>
    /// <param name="manifest">Parsed manifest (may be null if JSON was malformed).</param>
    /// <param name="pluginDirectory">Absolute path to the plugin directory.</param>
    /// <param name="seenIds">Set of plugin IDs already registered this startup (for duplicate detection).</param>
    public static string? Validate(
        Models.PluginManifest? manifest,
        string pluginDirectory,
        IReadOnlySet<string> seenIds)
    {
        if (manifest == null) return "Manifest is null after deserialization.";

        if (string.IsNullOrWhiteSpace(manifest.Id))           return "Field 'id' is required.";
        if (manifest.Id.IndexOfAny(PathSeparators) >= 0 || manifest.Id.Contains(".."))
            return "Field 'id' must not contain path separators or '..'";

        if (string.IsNullOrWhiteSpace(manifest.Name))         return "Field 'name' is required.";
        if (string.IsNullOrWhiteSpace(manifest.Version))      return "Field 'version' is required.";
        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion)) return "Field 'minHostVersion' is required.";
        if (string.IsNullOrWhiteSpace(manifest.MaxHostVersion)) return "Field 'maxHostVersion' is required.";
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) return "Field 'entryAssembly' is required.";
        if (string.IsNullOrWhiteSpace(manifest.EntryType))    return "Field 'entryType' is required.";
        if (manifest.EntryType.IndexOfAny(PathSeparators) >= 0 || manifest.EntryType.Contains(".."))
            return "Field 'entryType' must not contain path separators or '..'";

        if (string.IsNullOrWhiteSpace(manifest.Author))       return "Field 'author' is required.";
        if (string.IsNullOrWhiteSpace(manifest.Description))  return "Field 'description' is required.";

        // Duplicate ID check
        if (seenIds.Contains(manifest.Id))
            return $"Duplicate plugin ID '{manifest.Id}'. First occurrence wins; this one is rejected.";

        // Version must be parseable as System.Version
        if (!Version.TryParse(manifest.Version, out _))
            return $"Field 'version' value '{manifest.Version}' is not a valid semantic version (major.minor.patch).";

        // Path traversal guard on entryAssembly
        if (manifest.EntryAssembly.IndexOfAny(PathSeparators) >= 0 ||
            manifest.EntryAssembly.Contains(".."))
            return $"Field 'entryAssembly' must be a filename only, not a path: '{manifest.EntryAssembly}'.";

        // entryAssembly file must exist in the plugin directory
        var dllPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
        if (!File.Exists(dllPath))
            return $"Entry assembly '{manifest.EntryAssembly}' not found in '{pluginDirectory}'.";

        // No duplicate permissions
        if (manifest.Permissions.Count != manifest.Permissions.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Field 'permissions' contains duplicate values.";

        // No duplicate dependencies
        if (manifest.Dependencies.Count != manifest.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Field 'dependencies' contains duplicate values.";

        // No duplicate capabilities
        if (manifest.Capabilities.Count != manifest.Capabilities.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Field 'capabilities' contains duplicate values";

        return null;
    }
}
