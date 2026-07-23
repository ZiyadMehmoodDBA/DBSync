using MSOSync.Plugin.Packaging.Models;

namespace MSOSync.Plugin.Packaging;

/// <summary>
/// Validates a parsed <see cref="ManifestV2"/>. Returns null on success, or an error message on the first failure.
/// </summary>
public static class ManifestV2Validator
{
    private static readonly char[] PathSeparators = ['/', '\\'];
    private static readonly System.Text.RegularExpressions.Regex HexRegex =
        new("^[0-9a-f]{64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Validates all structural fields of a <see cref="ManifestV2"/> but skips the files[] content check.
    /// Used by <see cref="MSOSync.Plugin.Packaging.Packager.PluginPackager"/> before file hashes are computed.
    /// </summary>
    public static string? ValidateStructure(ManifestV2? manifest)
        => ValidateCore(manifest, skipFilesCheck: true);

    /// <summary>
    /// Validates all fields including files[]. Returns null on success or an error message on failure.
    /// </summary>
    public static string? Validate(ManifestV2? manifest)
        => ValidateCore(manifest, skipFilesCheck: false);

    private static string? ValidateCore(ManifestV2? manifest, bool skipFilesCheck)
    {
        if (manifest is null) return "Manifest is null after deserialization.";

        if (manifest.ManifestVersion != 2)
            return "Field 'manifestVersion' must be 2 for packaged plugins.";

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return "Field 'id' is required.";
        if (manifest.Id.IndexOfAny(PathSeparators) >= 0 || manifest.Id.Contains(".."))
            return "Field 'id' must not contain path separators or '..'.";

        if (string.IsNullOrWhiteSpace(manifest.Name))
            return "Field 'name' is required.";

        if (string.IsNullOrWhiteSpace(manifest.Version))
            return "Field 'version' is required.";
        if (!Version.TryParse(manifest.Version, out _))
            return $"Field 'version' value '{manifest.Version}' is not a valid version (major.minor.patch).";

        if (string.IsNullOrWhiteSpace(manifest.SdkVersion))
            return "Field 'sdkVersion' is required.";

        if (string.IsNullOrWhiteSpace(manifest.SdkVersionConstraint))
            return "Field 'sdkVersionConstraint' is required.";

        if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
            return "Field 'apiVersion' is required.";
        if (!int.TryParse(manifest.ApiVersion, out _))
            return $"Field 'apiVersion' value '{manifest.ApiVersion}' is not a valid integer string.";

        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion))
            return "Field 'minHostVersion' is required.";
        if (string.IsNullOrWhiteSpace(manifest.MaxHostVersion))
            return "Field 'maxHostVersion' is required.";

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            return "Field 'entryAssembly' is required.";
        if (manifest.EntryAssembly.IndexOfAny(PathSeparators) >= 0 || manifest.EntryAssembly.Contains(".."))
            return "Field 'entryAssembly' must be a filename only, not a path.";

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
            return "Field 'entryType' is required.";
        if (manifest.EntryType.IndexOfAny(PathSeparators) >= 0 || manifest.EntryType.Contains(".."))
            return "Field 'entryType' must not contain path separators or '..'.";

        if (string.IsNullOrWhiteSpace(manifest.Author))
            return "Field 'author' is required.";

        if (string.IsNullOrWhiteSpace(manifest.Description))
            return "Field 'description' is required.";

        if (manifest.Keywords.Count > 10)
            return "Field 'keywords' must have at most 10 entries.";

        // Validate files[]
        if (!skipFilesCheck)
        {
            if (manifest.Files.Count == 0)
                return "Field 'files' must contain at least one entry.";

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files)
            {
                if (string.IsNullOrWhiteSpace(entry.Path))
                    return "Each entry in 'files' must have a non-empty 'path'.";
                if (entry.Path.Contains("..") || System.IO.Path.IsPathRooted(entry.Path))
                    return $"Field 'files[].path' must be a relative path without '..': '{entry.Path}'.";
                if (!seenPaths.Add(entry.Path))
                    return $"Field 'files' contains duplicate path: '{entry.Path}'.";
                if (string.IsNullOrWhiteSpace(entry.Sha256) || !HexRegex.IsMatch(entry.Sha256))
                    return $"Field 'files[].sha256' for '{entry.Path}' must be exactly 64 lowercase hex characters.";
            }
        }

        // Validate pluginDependencies[]
        foreach (var dep in manifest.PluginDependencies)
        {
            if (string.IsNullOrWhiteSpace(dep.Id))
                return "Each entry in 'pluginDependencies' must have a non-empty 'id'.";
            if (string.IsNullOrWhiteSpace(dep.VersionRange))
                return $"'pluginDependencies[{dep.Id}].versionRange' is required.";
            if (SdkVersionConstraintParser.Parse(dep.VersionRange) is null)
                return $"'pluginDependencies[{dep.Id}].versionRange' value '{dep.VersionRange}' is not a valid semver range.";
        }

        return null;
    }
}
