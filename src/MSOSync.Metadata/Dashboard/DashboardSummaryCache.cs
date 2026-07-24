using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
using MSOSync.Metadata.Options;

namespace MSOSync.Metadata.Dashboard;

/// <summary>
/// In-process snapshot cache for DashboardSummaryDto.
/// TTL is configurable via Dashboard:SummaryTtlSeconds (default 30).
/// Cache key: CacheKeyHelper.OverviewSnapshot() — shared with the centralised key registry.
/// </summary>
public sealed class DashboardSummaryCache(
    ICacheService              cache,
    IOptions<DashboardOptions> options)
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.SummaryTtlSeconds);

    public async Task<DashboardSummaryDto> GetOrCreateAsync(
        Func<CancellationToken, Task<DashboardSummaryDto>> factory,
        CancellationToken ct)
    {
        var cached = await cache.GetAsync<DashboardSummaryDto>(CacheKeyHelper.OverviewSnapshot(), ct);
        if (cached is not null)
            return cached;

        var result = await factory(ct);
        await cache.SetAsync(CacheKeyHelper.OverviewSnapshot(), result, _ttl, ct);
        return result;
    }
}
