using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using System.Text.Json;

namespace MSOSync.Persistence.Stores;

public sealed class MarketplaceCacheStore(AppDbContext db) : IMarketplaceCacheStore
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private static string Normalize(string url) => url.TrimEnd('/');

    public async Task<IReadOnlyList<RegistryPluginEntry>?> GetSearchCacheAsync(
        string registryUrl, string cacheKey, CancellationToken ct)
    {
        var url = Normalize(registryUrl);
        var now = DateTime.UtcNow;

        // cacheKey is not stored per-row; we load all non-expired entries for this registry
        // and let MarketplaceService rebuild the paged result from the flat list.
        // This works for catalog sizes up to ~10 000 entries (deferred to 2C.3 otherwise).
        var rows = await db.MarketplaceCache
            .AsNoTracking()
            .Where(r => r.RegistryUrl == url && r.ExpiresAt > now)
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        return rows
            .Select(r => JsonSerializer.Deserialize<RegistryPluginEntry>(r.MetadataJson, JsonOpts))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    public async Task<RegistryPluginEntry?> GetPluginCacheAsync(
        string registryUrl, string pluginId, CancellationToken ct)
    {
        var url = Normalize(registryUrl);
        var now = DateTime.UtcNow;

        var row = await db.MarketplaceCache
            .AsNoTracking()
            .Where(r => r.RegistryUrl == url && r.PluginId == pluginId && r.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return JsonSerializer.Deserialize<RegistryPluginEntry>(row.MetadataJson, JsonOpts);
    }

    public async Task UpsertAsync(
        string registryUrl, RegistryPluginEntry entry, int cacheMinutes, CancellationToken ct)
    {
        var url  = Normalize(registryUrl);
        var now  = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(entry, JsonOpts);

        // Read without AsNoTracking so EF can track the entity for update
        var existing = await db.MarketplaceCache
            .Where(r => r.RegistryUrl == url && r.PluginId == entry.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            db.MarketplaceCache.Add(new SyncMarketplaceCache
            {
                RegistryUrl   = url,
                PluginId      = entry.Id,
                LatestVersion = entry.LatestVersion,
                MetadataJson  = json,
                CachedAt      = now,
                ExpiresAt     = now.AddMinutes(cacheMinutes)
            });
        }
        else
        {
            existing.LatestVersion = entry.LatestVersion;
            existing.MetadataJson  = json;
            existing.CachedAt      = now;
            existing.ExpiresAt     = now.AddMinutes(cacheMinutes);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBulkAsync(
        string registryUrl,
        IReadOnlyList<RegistryPluginEntry> entries,
        int cacheMinutes,
        CancellationToken ct)
    {
        // Sequential — must NOT use Task.WhenAll on a shared DbContext instance.
        foreach (var entry in entries)
            await UpsertAsync(registryUrl, entry, cacheMinutes, ct);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var now     = DateTime.UtcNow;
        var expired = await db.MarketplaceCache
            .Where(r => r.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0) return 0;

        db.MarketplaceCache.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
