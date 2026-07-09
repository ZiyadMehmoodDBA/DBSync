using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Moq;
using MSOSync.App.Workers;
using MSOSync.Metadata.Overview;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.AppTests.Overview;

public sealed class OverviewQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly WorkerStatusRegistry _registry;
    private readonly OverviewSnapshotCache _snapshotCache;
    private readonly IHostEnvironment _env;

    public OverviewQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _snapshotCache = new OverviewSnapshotCache(_cache);

        var publisherMock = new Mock<IPublisher>();
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _registry = new WorkerStatusRegistry(publisherMock.Object);

        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Test");
        _env = envMock.Object;
    }

    private OverviewQueryService CreateService()
        => new(_db, _registry, _snapshotCache, _env);

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

    // Test 5: Cache is invalidated after Invalidate() — second call returns new instance
    [Fact]
    public async Task GetAsync_AfterInvalidate_ReturnsNewInstance()
    {
        var svc = CreateService();
        var first = await svc.GetAsync(CancellationToken.None);
        _snapshotCache.Invalidate();
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

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }
}
