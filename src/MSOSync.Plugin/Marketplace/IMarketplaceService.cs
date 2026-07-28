using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Plugin.Marketplace;

/// <summary>
/// Fetches plugin catalog data from the configured remote registry.
/// Applies two-tier caching: IMemoryCache (L1) then DB cache (L2).
/// Registered as Scoped.
/// </summary>
public interface IMarketplaceService
{
    /// <summary>Search the registry catalog with optional text and category filters.</summary>
    Task<RegistrySearchResult> SearchAsync(
        string? query,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>
    /// Fetches full plugin details including all version history.
    /// Returns null when the plugin ID is not found.
    /// </summary>
    Task<RegistryPluginEntry?> GetPluginAsync(string pluginId, CancellationToken ct);

    /// <summary>Returns all versions for the given plugin. Returns empty list when not found.</summary>
    Task<IReadOnlyList<RegistryVersionEntry>> GetVersionsAsync(string pluginId, CancellationToken ct);

    /// <summary>
    /// Returns the latest version entry when it is newer than installedVersion.
    /// Returns null when the plugin is not in the registry or already at latest.
    /// </summary>
    Task<RegistryVersionEntry?> GetLatestUpdateAsync(
        string pluginId,
        string installedVersion,
        CancellationToken ct);

    /// <summary>
    /// Evicts the L1 (memory) cache entries for the given plugin and all search results.
    /// Call this after a plugin install or uninstall so that the next read reflects the
    /// current state rather than returning stale "available" data.
    /// </summary>
    void InvalidatePluginCache(string pluginId);
}
