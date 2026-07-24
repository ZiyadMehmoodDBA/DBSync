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
