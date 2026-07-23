using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.App.Workers;
using MSOSync.Common.Caching;
using MSOSync.Metadata.Overview;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.AppTests.Overview;

public sealed class OverviewQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MemoryCache _memCache;
    private readonly ICacheService _cache;
    private readonly WorkerStatusRegistry _registry;
    private readonly OverviewSnapshotCache _snapshotCache;
    private readonly IHostEnvironment _env;

    public OverviewQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db       = new AppDbContext(options);
        _memCache = new MemoryCache(new MemoryCacheOptions());
        var cacheOpts = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        _cache        = new InMemoryCacheService(_memCache, cacheOpts);
        _snapshotCache = new OverviewSnapshotCache(_cache);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        _registry = new WorkerStatusRegistry(scopeFactory.Object, NullLogger<WorkerStatusRegistry>.Instance);

        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Test");
        _env = envMock.Object;
    }

    private OverviewQueryService CreateService()
        => new(_db, new TestPlatformRepository<SyncAudit>(_db), _registry, _snapshotCache, _env);

    // Test 1: ClusterHealth = Healthy when all good (no nodes, healthy workers)
    [Fact]
    public async Task GetAsync_AllHealthy_ClusterHealthIsHealthy()
    {
        _registry.Register("Worker1", TimeSpan.FromSeconds(30));
        _registry.RecordTickStart("Worker1");
        _registry.RecordTickComplete("Worker1");

        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);

        Assert.Equal("Healthy", dto.Health.ClusterHealth);
    }

    // Test 2: ClusterHealth = Critical when >10% nodes are offline (Disabled/Decommissioned/Rejected)
    [Fact]
    public async Task GetAsync_ManyOfflineNodes_ClusterHealthIsCritical()
    {
        // Seed 10 nodes: 8 Active + 2 Disabled (20% offline > 10% threshold)
        for (int i = 0; i < 8; i++)
            _db.Nodes.Add(new SyncNode
            {
                NodeId = $"active-{i}",
                GroupId = "g1",
                SyncUrl = "http://localhost",
                LifecycleState = NodeLifecycleState.Active,
                NodeType = "T",
                ExternalId = $"ext-{i}",
                NodeName = $"Node{i}"
            });
        for (int i = 0; i < 2; i++)
            _db.Nodes.Add(new SyncNode
            {
                NodeId = $"disabled-{i}",
                GroupId = "g1",
                SyncUrl = "http://localhost",
                LifecycleState = NodeLifecycleState.Disabled,
                NodeType = "T",
                ExternalId = $"dis-ext-{i}",
                NodeName = $"DisabledNode{i}"
            });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);

        Assert.Equal("Critical", dto.Health.ClusterHealth);
    }

    // Test 3: ClusterHealth = Degraded when a worker is in Warning state
    [Fact]
    public async Task GetAsync_WorkerWarning_ClusterHealthIsDegraded()
    {
        _registry.Register("BadWorker", TimeSpan.FromSeconds(30));
        for (int i = 0; i < 3; i++)
        {
            _registry.RecordTickStart("BadWorker");
            _registry.RecordTickFailed("BadWorker", new Exception("oops"));
        }

        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);

        Assert.Equal("Degraded", dto.Health.ClusterHealth);
    }

    // Test 4: Cache is used on second call (same object reference)
    [Fact]
    public async Task GetAsync_SecondCall_ReturnsCachedResult()
    {
        var svc = CreateService();
        var first = await svc.GetAsync(CancellationToken.None);
        var second = await svc.GetAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    // Test 5: Cache is invalidated after InvalidateAsync() — second call returns new instance
    [Fact]
    public async Task GetAsync_AfterInvalidate_ReturnsNewInstance()
    {
        var svc = CreateService();
        var first = await svc.GetAsync(CancellationToken.None);
        await _snapshotCache.InvalidateAsync();
        var second = await svc.GetAsync(CancellationToken.None);

        Assert.NotSame(first, second);
    }

    // Test 6: No offline nodes => no NodeOffline warning entry
    [Fact]
    public async Task GetAsync_NoOfflineNodes_NoNodeOfflineWarning()
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId = "n1",
            GroupId = "g1",
            SyncUrl = "http://localhost",
            LifecycleState = NodeLifecycleState.Active,
            NodeType = "T",
            ExternalId = "ext-1",
            NodeName = "ActiveNode"
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);

        Assert.DoesNotContain(dto.Warnings, w => w.Type == "NodeOffline");
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentCacheMiss_FactoryCalledOnce()
    {
        var callCount = 0;
        var tcs = new TaskCompletionSource();

        async Task<OverviewDto> SlowFactory(CancellationToken ct)
        {
            Interlocked.Increment(ref callCount);
            await tcs.Task; // block until released
            return new OverviewDto(
                Health: new OverviewHealthWidget("Healthy", "Healthy", "Healthy"),
                Operations: new OverviewOperationsWidget(0, 0, 0, 0),
                Nodes: new OverviewNodesWidget(0, 0, 0, 0, 0, 0),
                Configuration: new OverviewConfigurationWidget(0, 0, 0),
                Warnings: [],
                RecentActivity: [],
                System: new OverviewSystemWidget("test", "M026", "Test", "0d 00:00:00",
                    "Configured", DateTime.UtcNow),
                LastRefreshedAt: DateTime.UtcNow);
        }

        // Fire 10 concurrent cache-miss requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _snapshotCache.GetOrCreateAsync(SlowFactory, CancellationToken.None))
            .ToArray();

        // Give all tasks time to reach the factory / semaphore
        await Task.Delay(50);
        tcs.SetResult(); // release the factory

        await Task.WhenAll(tasks);

        callCount.Should().Be(1, "only one request should reach the factory");
    }

    public void Dispose()
    {
        _db.Dispose();
        _memCache.Dispose();
    }
}

/// <summary>
/// In-memory IPlatformRepository&lt;T&gt; for unit tests.
/// Delegates to db.Set&lt;T&gt;().AsNoTracking() — no IgnoreQueryFilters needed in in-memory tests.
/// </summary>
internal sealed class TestPlatformRepository<T>(AppDbContext db) : IPlatformRepository<T>
    where T : class
{
    public IQueryable<T> QueryAll() => db.Set<T>().AsNoTracking();
}
