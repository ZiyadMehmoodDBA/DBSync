using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Loading;

public static class PluginDependencyResolver
{
    /// <summary>
    /// Verifies all declared dependencies are registered as Loaded in the registry.
    /// Plugins are processed in alphabetical order by directory name (one pass only).
    /// Limitation (14A): dependencies must sort alphabetically before the dependent plugin.
    /// Full dependency graph resolution is a 14B concern.
    /// </summary>
    /// <returns>Null if all dependencies are satisfied, or an error message.</returns>
    public static string? Resolve(PluginManifest manifest, IPluginRegistry registry)
    {
        foreach (var depId in manifest.Dependencies)
        {
            var dep = registry.GetById(depId);
            if (dep == null || dep.Status != PluginStatus.Loaded)
                return $"Dependency '{depId}' is not loaded. Ensure its directory name sorts alphabetically before '{manifest.Id}'.";
        }

        return null;
    }
}
