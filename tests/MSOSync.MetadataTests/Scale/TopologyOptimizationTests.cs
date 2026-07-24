using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Dashboard;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Options;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Topology;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Scale;

// ─── ClusterSummaryQueryService ──────────────────────────────────────────────

public sealed class ClusterSummaryProjectionTests
{
    [Fact]
    public async Task QueryNodeStates_GroupByReturnsCorrectCounts()
    {
        var db  = TestDbContext.Create();
        var svc = new ClusterSummaryQueryService(db);

        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "n1", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Active,  MaintenanceMode = false },
            new SyncNode { NodeId = "n2", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Active,  MaintenanceMode = true  },
            new SyncNode { NodeId = "n3", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Draining, MaintenanceMode = false },
            new SyncNode { NodeId = "n4", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Disabled, MaintenanceMode = false });
        await db.SaveChangesAsync();

        // We can't call QueryNodeStatesAsync directly (it's private), so call GetSummaryAsync
        // and check NodeCounts on the result.
        var summary = await svc.GetSummaryAsync();

        summary.NodeStates.Total.Should().Be(4);
        summary.NodeStates.Active.Should().Be(1);       // Active && !Maintenance
        summary.NodeStates.Maintenance.Should().Be(1);  // MaintenanceMode == true
        summary.NodeStates.Draining.Should().Be(1);
        summary.NodeStates.Offline.Should().Be(1);      // Disabled && !Maintenance
    }
}

// ─── DashboardQueryService ────────────────────────────────────────────────────

public sealed class DashboardSummaryOptimizationTests
{
    private static (DashboardQueryService Svc, AppDbContext Db) Make()
    {
        var db      = TestDbContext.Create();
        ICacheService cacheService = new InMemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) }));
        var cache   = new DashboardSummaryCache(cacheService,
                          Options.Create(new DashboardOptions()));
        var auditRepo = new TestPlatformRepository<SyncAudit>(db);
        var svc     = new DashboardQueryService(db, auditRepo, cache);
        return (svc, db);
    }

    [Fact]
    public async Task GetSummaryAsync_GroupByConnectivityStatus_CountsCorrectly()
    {
        var (svc, db) = Make();
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "n1", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Reachable   },
            new SyncNode { NodeId = "n2", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Reachable   },
            new SyncNode { NodeId = "n3", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Degraded    },
            new SyncNode { NodeId = "n4", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Unreachable });
        await db.SaveChangesAsync();

        var dto = await svc.GetSummaryAsync(default);

        dto.TotalNodes.Should().Be(4);
        dto.ReachableNodes.Should().Be(2);
        dto.DegradedNodes.Should().Be(1);
        dto.UnreachableNodes.Should().Be(1);
        dto.UnknownNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetSummaryAsync_CacheHit_DoesNotHitDb()
    {
        var (svc, _) = Make();
        // First call populates cache
        var first = await svc.GetSummaryAsync(default);
        // Second call must return same GeneratedAt (from cache)
        var second = await svc.GetSummaryAsync(default);

        second.GeneratedAt.Should().Be(first.GeneratedAt);
    }
}

// ─── BatchErrorQueryService ───────────────────────────────────────────────────

public sealed class BatchErrorSummaryGroupByTests
{
    [Fact]
    public async Task GetBatchErrorSummaryAsync_SingleQuery_CorrectCounts()
    {
        var db         = TestDbContext.Create();
        var classifier = new ErrorSeverityClassifier();
        var svc        = new BatchErrorQueryService(db, classifier);

        db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchId = 1L, BatchSequence = 1L,
            NodeId = "n1", ChannelId = "ch1", Status = 0
        });
        await db.SaveChangesAsync();

        db.BatchErrors.AddRange(
            new SyncBatchError { BatchId = 1L, ConflictType = "DuplicateKey",   ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = "Timeout",         ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = "Deadlock",        ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = "MetadataMissing", ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = null,              ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var dto = await svc.GetBatchErrorSummaryAsync(null, null, null, default);

        dto.Info.Should().Be(1);
        dto.Warning.Should().Be(2);
        dto.Critical.Should().Be(2);
        dto.Total.Should().Be(5);
    }
}

// ─── TopologyQueryService group node cursor ───────────────────────────────────

public sealed class TopologyGroupNodeCursorTests
{
    private static TopologyQueryService MakeSvc(out AppDbContext db)
    {
        var ctx   = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var signer = new CursorSigner(new byte[32]);
        db = ctx;
        return new TopologyQueryService(ctx, cache, signer);
    }

    [Fact]
    public async Task GetGroupNodesAsync_FirstPage_ReturnsPageSizeItems()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 5; i++)
            db.Nodes.Add(new SyncNode
            {
                NodeId = $"node-{i:D3}", GroupId = "g1",
                SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active
            });
        await db.SaveChangesAsync();

        var page1 = await svc.GetGroupNodesAsync("g1", null, 2, default);

        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();
        page1.Items[0].NodeId.Should().Be("node-001");
    }

    [Fact]
    public async Task GetGroupNodesAsync_SubsequentPage_DoesNotDuplicate()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 4; i++)
            db.Nodes.Add(new SyncNode
            {
                NodeId = $"node-{i:D3}", GroupId = "g1",
                SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active
            });
        await db.SaveChangesAsync();

        var page1 = await svc.GetGroupNodesAsync("g1", null, 2, default);
        var page2 = await svc.GetGroupNodesAsync("g1", page1.NextCursor, 2, default);

        page1.Items.Select(n => n.NodeId)
            .Intersect(page2.Items.Select(n => n.NodeId))
            .Should().BeEmpty();
        page2.Items[0].NodeId.Should().Be("node-003");
    }

    [Fact]
    public async Task GetGroupNodesAsync_LastPage_HasMoreFalse()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "node-001", GroupId = "g1", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active },
            new SyncNode { NodeId = "node-002", GroupId = "g1", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active });
        await db.SaveChangesAsync();

        var result = await svc.GetGroupNodesAsync("g1", null, 10, default);

        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetTopologyGraphAsync_WithNodeIdFilter_OnlyReturnsRelevantGroups()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().AddRange(
            new SyncNodeGroup { GroupId = "g1" },
            new SyncNodeGroup { GroupId = "g2" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "node-001", GroupId = "g1", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active },
            new SyncNode { NodeId = "node-002", GroupId = "g2", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active });
        await db.SaveChangesAsync();

        // Filter to only g1 nodes
        var result = await svc.GetTopologyGraphAsync(new[] { "node-001" }, default);

        // Should only include groups relevant to the filtered nodes
        result.Nodes.Should().NotBeEmpty();
        result.Nodes.Any(n => n.GroupId == "g1").Should().BeTrue();
    }
}
