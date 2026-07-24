using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Metadata.Options;

namespace MSOSync.Metadata.Dashboard;

/// <summary>
/// In-process snapshot cache for DashboardSummaryDto.
/// TTL is configurable via Dashboard:SummaryTtlSeconds (default 30).
/// Cache key: "dashboard:summary" (single-tenant; for multi-tenant, key per tenant).
/// </summary>
public sealed class DashboardSummaryCache(
    IMemoryCache               cache,
    IOptions<DashboardOptions> options)
{
    private const string CacheKey = "dashboard:summary";

    public async Task<DashboardSummaryDto> GetOrCreateAsync(
        Func<CancellationToken, Task<DashboardSummaryDto>> factory,
        CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out DashboardSummaryDto? cached))
            return cached!;

        var result = await factory(ct);

        var ttl = TimeSpan.FromSeconds(options.Value.SummaryTtlSeconds);
        cache.Set(CacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });

        return result;
    }
}
