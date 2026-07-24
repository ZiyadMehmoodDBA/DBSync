using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
using MSOSync.Metadata.Dashboard;
using MSOSync.Metadata.Options;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures DashboardQueryService.GetSummaryAsync (cache miss) at 1000 nodes with mixed statuses.
/// Target: P95 &lt; 100 ms for the full summary computation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class DashboardSummaryBenchmark
{
    private DashboardQueryService _svc    = null!;
    private DashboardSummaryCache _cache  = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();

        _cache = BuildCache();

        var db        = BenchmarkDbSeeder.CreateDb();
        var auditRepo = new NullPlatformRepository<SyncAudit>();
        _svc = new DashboardQueryService(db, auditRepo, _cache);
    }

    [Benchmark]
    public async Task GetSummary_CacheMiss()
    {
        // Invalidate cache before each iteration to measure DB cost
        // DashboardSummaryCache does not expose an invalidate method, so we create a fresh one
        var freshCache = BuildCache();
        var db        = BenchmarkDbSeeder.CreateDb();
        var svc       = new DashboardQueryService(db, new NullPlatformRepository<SyncAudit>(), freshCache);
        _ = await svc.GetSummaryAsync(default);
    }

    [Benchmark]
    public async Task GetSummary_CacheHit()
    {
        // Warm up once, then measure cache hit
        _ = await _svc.GetSummaryAsync(default);
    }

    private static DashboardSummaryCache BuildCache()
    {
        var memCache = new MemoryCache(new MemoryCacheOptions());
        ICacheService cacheService = new InMemoryCacheService(memCache, Options.Create(new CacheOptions()));
        return new DashboardSummaryCache(cacheService,
            Options.Create(new DashboardOptions { SummaryTtlSeconds = 30 }));
    }
}

/// <summary>No-op IPlatformRepository for benchmarks (no audit rows needed).</summary>
internal sealed class NullPlatformRepository<T> : IPlatformRepository<T> where T : class
{
    private static readonly IQueryable<T> Empty = Enumerable.Empty<T>().AsQueryable();
    public IQueryable<T> QueryAll() => Empty;
}
