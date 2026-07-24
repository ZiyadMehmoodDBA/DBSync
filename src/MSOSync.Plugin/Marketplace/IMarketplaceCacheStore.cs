using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Plugin.Marketplace;

/// <summary>
/// Persistence layer bridge for the marketplace DB cache.
/// Implemented in MSOSync.Persistence. Injected via DI (Scoped).
/// </summary>
public interface IMarketplaceCacheStore
{
    /// <summary>
    /// Returns all non-expired cache entries for the given registry URL.
    /// Returns null when no valid cache entry exists.
    /// </summary>
    Task<IReadOnlyList<RegistryPluginEntry>?> GetSearchCacheAsync(
        string registryUrl,
        string cacheKey,
        CancellationToken ct);

    /// <summary>
    /// Returns a single non-expired cache entry for the given plugin ID.
    /// Returns null when no valid entry exists or it is expired.
    /// </summary>
    Task<RegistryPluginEntry?> GetPluginCacheAsync(
        string registryUrl,
        string pluginId,
        CancellationToken ct);

    /// <summary>Upserts a cache entry for a single plugin.</summary>
    Task UpsertAsync(
        string registryUrl,
        RegistryPluginEntry entry,
        int cacheMinutes,
        CancellationToken ct);

    /// <summary>
    /// Bulk upsert. Must NOT use Task.WhenAll — iterates sequentially
    /// to avoid concurrent writes on a shared DbContext.
    /// </summary>
    Task UpsertBulkAsync(
        string registryUrl,
        IReadOnlyList<RegistryPluginEntry> entries,
        int cacheMinutes,
        CancellationToken ct);

    /// <summary>Deletes all expired rows for all registries. Returns row count deleted.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct);
}
