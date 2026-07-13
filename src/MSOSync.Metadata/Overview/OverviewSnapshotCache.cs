using Microsoft.Extensions.Caching.Memory;

namespace MSOSync.Metadata.Overview;

public sealed class OverviewSnapshotCache(IMemoryCache cache)
{
    private const string Key = "overview_snapshot";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    public bool TryGet(out OverviewDto? dto)
        => cache.TryGetValue(Key, out dto);

    public void Set(OverviewDto dto)
        => cache.Set(Key, dto, Ttl);

    public void Invalidate()
        => cache.Remove(Key);
}
