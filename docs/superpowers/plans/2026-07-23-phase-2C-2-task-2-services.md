# Task 2 — Services

**Plan:** `2026-07-23-phase-2C-2-master.md`
**Scope:** `MarketplaceOptions`, remote registry models, all service interfaces, `MarketplaceCacheStore`, `MarketplaceService`, `PluginUpdateService`, `MarketplaceLogEvents`.

---

## Step 2.1 — `MarketplaceOptions`

- [ ] Create `src/MSOSync.Plugin/Marketplace/MarketplaceOptions.cs`:

```csharp
namespace MSOSync.Plugin.Marketplace;

public sealed class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    /// <summary>
    /// Base URL of the remote registry.
    /// When null or empty, all marketplace endpoints return 503.
    /// </summary>
    public string? RegistryUrl { get; set; }

    /// <summary>
    /// Optional API key sent in the X-Api-Key header.
    /// Leave null for public registries.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Minutes to retain remote results in the local DB cache. Default: 60.</summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>Minutes to retain search results in IMemoryCache. Default: 5.</summary>
    public int MemoryCacheMinutes { get; set; } = 5;

    /// <summary>HTTP timeout in seconds for registry calls. Default: 30.</summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>Polly retry attempts on transient HTTP failures. Default: 3.</summary>
    public int RetryCount { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(RegistryUrl);
}
```

---

## Step 2.2 — Remote registry models

- [ ] Create `src/MSOSync.Plugin/Marketplace/Models/RegistryPluginEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>Single plugin entry from the remote registry catalog.</summary>
public sealed record RegistryPluginEntry
{
    [JsonPropertyName("id")]             public string   Id             { get; init; } = null!;
    [JsonPropertyName("name")]           public string   Name           { get; init; } = null!;
    [JsonPropertyName("author")]         public string   Author         { get; init; } = null!;
    [JsonPropertyName("description")]    public string   Description    { get; init; } = null!;
    [JsonPropertyName("category")]       public string   Category       { get; init; } = null!;
    [JsonPropertyName("tags")]           public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("latestVersion")]  public string   LatestVersion  { get; init; } = null!;
    [JsonPropertyName("minHostVersion")] public string   MinHostVersion { get; init; } = null!;
    [JsonPropertyName("downloadCount")]  public long     DownloadCount  { get; init; }
    [JsonPropertyName("rating")]         public double   Rating         { get; init; }
    [JsonPropertyName("ratingCount")]    public int      RatingCount    { get; init; }
    [JsonPropertyName("publishedAt")]    public DateTime PublishedAt    { get; init; }
    [JsonPropertyName("updatedAt")]      public DateTime UpdatedAt      { get; init; }
    [JsonPropertyName("iconUrl")]        public string?  IconUrl        { get; init; }
    [JsonPropertyName("projectUrl")]     public string?  ProjectUrl     { get; init; }
    [JsonPropertyName("licenseId")]      public string?  LicenseId      { get; init; }
    [JsonPropertyName("verified")]       public bool     Verified       { get; init; }
    [JsonPropertyName("versions")]       public IReadOnlyList<RegistryVersionEntry> Versions { get; init; } = [];
}
```

- [ ] Create `src/MSOSync.Plugin/Marketplace/Models/RegistryVersionEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

public sealed record RegistryVersionEntry
{
    [JsonPropertyName("version")]        public string   Version        { get; init; } = null!;
    [JsonPropertyName("minHostVersion")] public string   MinHostVersion { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")] public string   MaxHostVersion { get; init; } = null!;
    [JsonPropertyName("publishedAt")]    public DateTime PublishedAt    { get; init; }
    [JsonPropertyName("downloadUrl")]    public string   DownloadUrl    { get; init; } = null!;
    [JsonPropertyName("sha256")]         public string   Sha256         { get; init; } = null!;
    [JsonPropertyName("releaseNotes")]   public string?  ReleaseNotes   { get; init; }
    [JsonPropertyName("deprecated")]     public bool     Deprecated     { get; init; }
}
```

- [ ] Create `src/MSOSync.Plugin/Marketplace/Models/RegistrySearchResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>Paged search result envelope from the remote registry search endpoint.</summary>
public sealed record RegistrySearchResult
{
    [JsonPropertyName("data")]       public IReadOnlyList<RegistryPluginEntry> Data       { get; init; } = [];
    [JsonPropertyName("total")]      public int Total      { get; init; }
    [JsonPropertyName("page")]       public int Page       { get; init; }
    [JsonPropertyName("pageSize")]   public int PageSize   { get; init; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; init; }
}
```

- [ ] Create `src/MSOSync.Plugin/Marketplace/Models/PluginUpdateManifest.cs`:

```csharp
namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>Describes an available update for an installed plugin.</summary>
public sealed record PluginUpdateManifest(
    string   PluginId,
    string   InstalledVersion,
    string   AvailableVersion,
    string   DownloadUrl,
    string   Sha256,
    string?  ReleaseNotes,
    DateTime PublishedAt);
```

---

## Step 2.3 — `MarketplaceLogEvents`

- [ ] Create `src/MSOSync.Plugin/Marketplace/MarketplaceLogEvents.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace MSOSync.Plugin.Marketplace;

/// <summary>Structured log event IDs for the marketplace subsystem.</summary>
public static class MarketplaceLogEvents
{
    public static readonly EventId SearchFetched       = new(2001, "Marketplace2001");
    public static readonly EventId PluginDetailFetched = new(2002, "Marketplace2002");
    public static readonly EventId SearchFailed        = new(2003, "Marketplace2003");
    public static readonly EventId PluginFetchFailed   = new(2004, "Marketplace2004");
    public static readonly EventId CacheWritten        = new(2005, "Marketplace2005");
    public static readonly EventId CacheMiss           = new(2006, "Marketplace2006");
    public static readonly EventId InstallTriggered    = new(2007, "Marketplace2007");
    public static readonly EventId BulkUpdateChecked   = new(2008, "Marketplace2008");
    public static readonly EventId ExpiredPurged       = new(2009, "Marketplace2009");
}
```

---

## Step 2.4 — Service interfaces

- [ ] Create `src/MSOSync.Plugin/Marketplace/IMarketplaceCacheStore.cs`:

```csharp
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
```

- [ ] Create `src/MSOSync.Plugin/Marketplace/IMarketplaceService.cs`:

```csharp
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
}
```

- [ ] Create `src/MSOSync.Plugin/Marketplace/IPluginUpdateService.cs`:

```csharp
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Plugin.Marketplace;

/// <summary>
/// Compares locally installed plugin versions against the remote registry.
/// Registered as Scoped.
/// </summary>
public interface IPluginUpdateService
{
    /// <summary>
    /// Checks a single installed plugin for an available update.
    /// Returns null when the plugin is not in the registry or is already at latest.
    /// </summary>
    Task<PluginUpdateManifest?> CheckAsync(
        string pluginId,
        string installedVersion,
        CancellationToken ct);

    /// <summary>
    /// Checks all currently installed plugins for updates.
    /// Iterates IPluginStore.GetAllAsync and calls CheckAsync for each sequentially
    /// (no Task.WhenAll). Plugins not in the registry are silently skipped.
    /// </summary>
    Task<IReadOnlyList<PluginUpdateManifest>> CheckAllAsync(CancellationToken ct);
}
```

---

## Step 2.5 — `MarketplaceCacheStore`

- [ ] Create `src/MSOSync.Persistence/Stores/MarketplaceCacheStore.cs`:

```csharp
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
```

---

## Step 2.6 — `MarketplaceService`

- [ ] Create `src/MSOSync.Metadata/Marketplace/MarketplaceService.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace MSOSync.Metadata.Marketplace;

public sealed class MarketplaceService(
    IHttpClientFactory           httpClientFactory,
    IMarketplaceCacheStore       cacheStore,
    IMemoryCache                 memoryCache,
    IOptions<MarketplaceOptions> options,
    ILogger<MarketplaceService>  logger) : IMarketplaceService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private MarketplaceOptions Opts => options.Value;

    public async Task<RegistrySearchResult> SearchAsync(
        string? query, string? category, int page, int pageSize, CancellationToken ct)
    {
        var cacheKey = BuildSearchCacheKey(query, category, page, pageSize);
        var memKey   = $"marketplace:search:{cacheKey}";

        // L1: memory cache
        if (memoryCache.TryGetValue(memKey, out RegistrySearchResult? cached) && cached is not null)
            return cached;

        // L2: DB cache — loads all non-expired entries then rebuilds paged result in memory
        var dbEntries = await cacheStore.GetSearchCacheAsync(Opts.RegistryUrl!, cacheKey, ct);
        if (dbEntries is not null)
        {
            var result = BuildPagedResult(dbEntries, page, pageSize);
            memoryCache.Set(memKey, result, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return result;
        }

        // L3: remote
        logger.Log(LogLevel.Debug, MarketplaceLogEvents.CacheMiss,
            "DB cache miss — fetching search from remote. Key: {CacheKey}", cacheKey);
        return await FetchSearchAsync(query, category, page, pageSize, memKey, ct);
    }

    public async Task<RegistryPluginEntry?> GetPluginAsync(string pluginId, CancellationToken ct)
    {
        var memKey = $"marketplace:plugin:{pluginId}";

        if (memoryCache.TryGetValue(memKey, out RegistryPluginEntry? cached) && cached is not null)
            return cached;

        var dbEntry = await cacheStore.GetPluginCacheAsync(Opts.RegistryUrl!, pluginId, ct);
        if (dbEntry is not null)
        {
            memoryCache.Set(memKey, dbEntry, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return dbEntry;
        }

        logger.Log(LogLevel.Debug, MarketplaceLogEvents.CacheMiss,
            "DB cache miss — fetching plugin from remote. PluginId: {PluginId}", pluginId);
        return await FetchPluginAsync(pluginId, memKey, ct);
    }

    public async Task<IReadOnlyList<RegistryVersionEntry>> GetVersionsAsync(
        string pluginId, CancellationToken ct)
    {
        var entry = await GetPluginAsync(pluginId, ct);
        return entry?.Versions ?? [];
    }

    public async Task<RegistryVersionEntry?> GetLatestUpdateAsync(
        string pluginId, string installedVersion, CancellationToken ct)
    {
        var entry = await GetPluginAsync(pluginId, ct);
        if (entry is null) return null;
        if (!IsNewer(entry.LatestVersion, installedVersion)) return null;
        return entry.Versions.FirstOrDefault(v => v.Version == entry.LatestVersion);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<RegistrySearchResult> FetchSearchAsync(
        string? query, string? category, int page, int pageSize,
        string memKey, CancellationToken ct)
    {
        var sw     = Stopwatch.StartNew();
        var client = httpClientFactory.CreateClient("MarketplaceRegistry");
        var url    = BuildSearchUrl(query, category, page, pageSize);

        try
        {
            var response = await client.GetFromJsonAsync<RegistrySearchResult>(url, JsonOpts, ct)
                           ?? new RegistrySearchResult();

            sw.Stop();
            logger.Log(LogLevel.Information, MarketplaceLogEvents.SearchFetched,
                "Remote registry search fetched. Page: {Page}, Total: {Total}, ElapsedMs: {Elapsed}",
                response.Page, response.Total, sw.ElapsedMilliseconds);

            if (Opts.CacheMinutes > 0)
            {
                await cacheStore.UpsertBulkAsync(
                    Opts.RegistryUrl!, response.Data, Opts.CacheMinutes, ct);
                logger.Log(LogLevel.Debug, MarketplaceLogEvents.CacheWritten,
                    "DB cache written for {Count} entries.", response.Data.Count);
            }

            memoryCache.Set(memKey, response, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return response;
        }
        catch (Exception ex)
        {
            logger.Log(LogLevel.Warning, MarketplaceLogEvents.SearchFailed, ex,
                "Marketplace registry search failed. Url: {Url}", url);
            return new RegistrySearchResult();
        }
    }

    private async Task<RegistryPluginEntry?> FetchPluginAsync(
        string pluginId, string memKey, CancellationToken ct)
    {
        var sw     = Stopwatch.StartNew();
        var client = httpClientFactory.CreateClient("MarketplaceRegistry");
        var url    = $"plugins/{Uri.EscapeDataString(pluginId)}";

        try
        {
            var entry = await client.GetFromJsonAsync<RegistryPluginEntry>(url, JsonOpts, ct);
            if (entry is null) return null;

            sw.Stop();
            logger.Log(LogLevel.Information, MarketplaceLogEvents.PluginDetailFetched,
                "Remote registry plugin detail fetched. PluginId: {PluginId}, ElapsedMs: {Elapsed}",
                pluginId, sw.ElapsedMilliseconds);

            if (Opts.CacheMinutes > 0)
            {
                await cacheStore.UpsertAsync(Opts.RegistryUrl!, entry, Opts.CacheMinutes, ct);
                logger.Log(LogLevel.Debug, MarketplaceLogEvents.CacheWritten,
                    "DB cache written. PluginId: {PluginId}, ExpiresAt: {ExpiresAt}",
                    pluginId, DateTime.UtcNow.AddMinutes(Opts.CacheMinutes));
            }

            memoryCache.Set(memKey, entry, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return entry;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.Log(LogLevel.Warning, MarketplaceLogEvents.PluginFetchFailed, ex,
                "Marketplace registry fetch failed. PluginId: {PluginId}", pluginId);
            return null;
        }
    }

    private string BuildSearchUrl(string? query, string? category, int page, int pageSize)
    {
        var q = $"plugins?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(query))
            q += $"&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(category))
            q += $"&category={Uri.EscapeDataString(category)}";
        return q;
    }

    private static string BuildSearchCacheKey(
        string? query, string? category, int page, int pageSize) =>
        $"{query}|{category}|{page}|{pageSize}".ToLowerInvariant();

    private static RegistrySearchResult BuildPagedResult(
        IReadOnlyList<RegistryPluginEntry> entries, int page, int pageSize)
    {
        var total      = entries.Count;
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var data       = entries.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new RegistrySearchResult
        {
            Data       = data,
            Total      = total,
            Page       = page,
            PageSize   = pageSize,
            TotalPages = totalPages
        };
    }

    /// <summary>Returns true if candidateVersion is strictly greater than baseVersion.</summary>
    private static bool IsNewer(string candidateVersion, string baseVersion)
    {
        if (!Version.TryParse(candidateVersion, out var candidate)) return false;
        if (!Version.TryParse(baseVersion,      out var @base))     return false;
        return candidate > @base;
    }
}
```

---

## Step 2.7 — `PluginUpdateService`

- [ ] Create `src/MSOSync.Metadata/Marketplace/PluginUpdateService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Metadata.Marketplace;

public sealed class PluginUpdateService(
    IMarketplaceService         marketplaceService,
    IPluginStore                pluginStore,
    ILogger<PluginUpdateService> logger) : IPluginUpdateService
{
    public async Task<PluginUpdateManifest?> CheckAsync(
        string pluginId, string installedVersion, CancellationToken ct)
    {
        var latestEntry = await marketplaceService.GetLatestUpdateAsync(
            pluginId, installedVersion, ct);

        if (latestEntry is null) return null;

        return new PluginUpdateManifest(
            pluginId,
            installedVersion,
            latestEntry.Version,
            latestEntry.DownloadUrl,
            latestEntry.Sha256,
            latestEntry.ReleaseNotes,
            latestEntry.PublishedAt);
    }

    public async Task<IReadOnlyList<PluginUpdateManifest>> CheckAllAsync(CancellationToken ct)
    {
        var installed = await pluginStore.GetAllAsync(ct);
        var results   = new List<PluginUpdateManifest>(installed.Count);

        // Sequential — no Task.WhenAll (would saturate registry HTTP or share DbContext)
        foreach (var record in installed)
        {
            var manifest = await CheckAsync(record.PluginId, record.PluginVersion, ct);
            if (manifest is not null)
                results.Add(manifest);
        }

        logger.Log(LogLevel.Information, MarketplaceLogEvents.BulkUpdateChecked,
            "Bulk update check completed. TotalChecked: {Total}, UpdatesFound: {Found}",
            installed.Count, results.Count);

        return results;
    }
}
```

---

## Step 2.8 — Build check

- [ ] Run:

```powershell
dotnet build src/MSOSync.Plugin/MSOSync.Plugin.csproj --no-restore
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj --no-restore
dotnet build src/MSOSync.Metadata/MSOSync.Metadata.csproj --no-restore
```

All three must build with 0 errors.

- [ ] Confirm no illegal project references:

```powershell
Select-String -Path "src/MSOSync.Plugin/MSOSync.Plugin.csproj" -Pattern "MSOSync.Persistence"
Select-String -Path "src/MSOSync.Metadata/MSOSync.Metadata.csproj" -Pattern "MSOSync.Batch|MSOSync.Routing"
```

Both commands must return no matches.
