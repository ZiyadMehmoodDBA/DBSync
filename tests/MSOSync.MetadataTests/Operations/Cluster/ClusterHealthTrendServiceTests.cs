using FluentAssertions;
using MSOSync.Metadata.Operations.Cluster.HealthTrends;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Cluster;

public sealed class ClusterHealthTrendServiceTests : IDisposable
{
    private readonly global::MSOSync.Persistence.AppDbContext _db = TestDbContext.Create();
    private readonly ClusterHealthTrendService _svc;

    public ClusterHealthTrendServiceTests()
    {
        _svc = new ClusterHealthTrendService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncNode MakeNode(string nodeId) => new()
    {
        NodeId     = nodeId,
        NodeName   = nodeId,
        GroupId    = "g1",
        SyncUrl    = "http://n",
        LifecycleState = NodeLifecycleState.Active,
        TenantId   = Guid.Empty,
    };

    [Theory]
    [InlineData("1h",  12)]
    [InlineData("6h",  12)]
    [InlineData("24h", 12)]
    [InlineData("7d",  14)]
    public async Task GetTrendsAsync_AllWindows_ReturnCorrectBucketCount(string window, int expected)
    {
        var result = await _svc.GetTrendsAsync(window, null, default);
        result.BucketCount.Should().Be(expected);
        result.Buckets.Should().HaveCount(expected);
        result.Window.Should().Be(window);
    }

    [Fact]
    public async Task GetTrendsAsync_NoHistory_AllBucketsZeroAndNodeStatsEmpty()
    {
        var result = await _svc.GetTrendsAsync("6h", null, default);
        result.Buckets.Should().AllSatisfy(b =>
        {
            b.ReachableCount.Should().Be(0);
            b.DegradedCount.Should().Be(0);
            b.UnreachableCount.Should().Be(0);
            b.TransitionCount.Should().Be(0);
        });
        result.NodeProbeStats.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendsAsync_NodeAllReachable_UptimePct100()
    {
        var tenantId = Guid.NewGuid();
        _db.Nodes.Add(MakeNode("n1"));
        await _db.SaveChangesAsync();

        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "n1", PreviousStatus = ConnectivityStatus.Unknown, NewStatus = ConnectivityStatus.Reachable, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n1", PreviousStatus = ConnectivityStatus.Reachable, NewStatus = ConnectivityStatus.Reachable, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),  TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        var nodeStat = result.NodeProbeStats.FirstOrDefault(n => n.NodeId == "n1");
        nodeStat.Should().NotBeNull();
        nodeStat!.UptimePct.Should().Be(100.0);
        nodeStat.ConsecutiveProbeFailures.Should().Be(0);
    }

    [Fact]
    public async Task GetTrendsAsync_NodeMixedStatus_UptimePct50()
    {
        var tenantId = Guid.NewGuid();
        _db.Nodes.Add(MakeNode("n2"));
        await _db.SaveChangesAsync();

        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "n2", PreviousStatus = ConnectivityStatus.Unknown,     NewStatus = ConnectivityStatus.Reachable,    OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-20), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n2", PreviousStatus = ConnectivityStatus.Reachable,   NewStatus = ConnectivityStatus.Unreachable,  OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-15), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        var nodeStat = result.NodeProbeStats.FirstOrDefault(n => n.NodeId == "n2");
        nodeStat!.UptimePct.Should().Be(50.0);
        nodeStat.ConsecutiveProbeFailures.Should().Be(1);
        nodeStat.ConnectivityStatus.Should().Be("Unreachable");
    }

    [Fact]
    public async Task GetTrendsAsync_NodeIdFilter_ScopesToSingleNode()
    {
        var tenantId = Guid.NewGuid();
        _db.Nodes.AddRange(MakeNode("nA"), MakeNode("nB"));
        await _db.SaveChangesAsync();

        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "nA", NewStatus = ConnectivityStatus.Reachable, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "nB", NewStatus = ConnectivityStatus.Degraded,  OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", "nA", default);

        result.NodeProbeStats.Should().HaveCount(1);
        result.NodeProbeStats[0].NodeId.Should().Be("nA");
    }

    [Fact]
    public async Task GetTrendsAsync_OldHistoryOutsideWindow_Excluded()
    {
        var tenantId = Guid.NewGuid();
        _db.Nodes.Add(MakeNode("old"));
        await _db.SaveChangesAsync();

        _db.Set<SyncNodeConnectivityHistory>().Add(
            new SyncNodeConnectivityHistory { NodeId = "old", NewStatus = ConnectivityStatus.Unreachable, OccurredAt = DateTimeOffset.UtcNow.AddHours(-3), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        result.NodeProbeStats.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendsAsync_ConsecutiveFailures_CountedFromMostRecent()
    {
        var tenantId = Guid.NewGuid();
        _db.Nodes.Add(MakeNode("n3"));
        await _db.SaveChangesAsync();

        // Reachable, then 2 consecutive failures
        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "n3", NewStatus = ConnectivityStatus.Reachable,    OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-30), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n3", NewStatus = ConnectivityStatus.Degraded,     OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-20), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n3", NewStatus = ConnectivityStatus.Unreachable,  OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        var stat = result.NodeProbeStats.First(n => n.NodeId == "n3");
        stat.ConsecutiveProbeFailures.Should().Be(2);
    }
}
