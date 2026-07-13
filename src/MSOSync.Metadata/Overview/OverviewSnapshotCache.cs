using Microsoft.Extensions.Caching.Memory;

namespace MSOSync.Metadata.Overview;

public sealed class OverviewSnapshotCache(IMemoryCache cache)
{
    private const string Key = "overview_snapshot";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public bool TryGet(out OverviewDto? dto)
        => cache.TryGetValue(Key, out dto);

    public void Set(OverviewDto dto)
        => cache.Set(Key, dto, Ttl);

    public void Invalidate()
        => cache.Remove(Key);

    public async Task<OverviewDto> GetOrCreateAsync(
        Func<CancellationToken, Task<OverviewDto>> factory, CancellationToken ct)
    {
        if (cache.TryGetValue(Key, out OverviewDto? dto) && dto is not null)
            return dto;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (cache.TryGetValue(Key, out dto) && dto is not null)
                return dto;

            dto = await factory(ct);
            cache.Set(Key, dto, Ttl);
            return dto;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
