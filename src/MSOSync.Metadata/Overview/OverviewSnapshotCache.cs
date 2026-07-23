using MSOSync.Common.Caching;

namespace MSOSync.Metadata.Overview;

public sealed class OverviewSnapshotCache(ICacheService cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task InvalidateAsync(CancellationToken ct = default)
        => await cache.RemoveAsync(CacheKeyHelper.OverviewSnapshot(), ct);

    public async Task<OverviewDto> GetOrCreateAsync(
        Func<CancellationToken, Task<OverviewDto>> factory, CancellationToken ct)
    {
        var dto = await cache.GetAsync<OverviewDto>(CacheKeyHelper.OverviewSnapshot(), ct);
        if (dto is not null)
            return dto;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            dto = await cache.GetAsync<OverviewDto>(CacheKeyHelper.OverviewSnapshot(), ct);
            if (dto is not null)
                return dto;

            dto = await factory(ct);
            await cache.SetAsync(CacheKeyHelper.OverviewSnapshot(), dto, Ttl, ct);
            return dto;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
