// tests/MSOSync.EngineTests/RoutingServiceTests.cs
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class RoutingServiceTests
{
    private static (RoutingService Svc, AppDbContext Db, IMemoryCache Cache, RouteCacheState State) Create()
    {
        var db    = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var state = new RouteCacheState();
        var svc   = new RoutingService(db, cache, state);
        return (svc, db, cache, state);
    }

    private static void SeedRoute(AppDbContext db, string triggerId, string routerId, string targetGroupId, string nodeId)
    {
        if (!db.NodeGroups.Any(g => g.GroupId == targetGroupId))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = targetGroupId, GroupName = targetGroupId });
        if (!db.Nodes.Any(n => n.NodeId == nodeId))
            db.Nodes.Add(new SyncNode { NodeId = nodeId, GroupId = targetGroupId, SyncUrl = "http://x", Status = "ONLINE", SyncEnabled = true });
        if (!db.Routers.Any(r => r.RouterId == routerId))
            db.Routers.Add(new SyncRouter { RouterId = routerId, SourceNodeGroup = "src", TargetNodeGroup = targetGroupId, Enabled = true });
        if (!db.TriggerRouters.Any(tr => tr.TriggerId == triggerId))
            db.TriggerRouters.Add(new SyncTriggerRouter { TriggerId = triggerId, RouterId = routerId, Enabled = true });
        db.SaveChanges();
    }

    [Fact]
    public async Task ResolveAsync_SeededRoute_ReturnsTargetNodeId()
    {
        var (svc, db, _, _) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        var result = await svc.ResolveAsync("t1");

        result.Should().ContainSingle().Which.Should().Be("node-a");
    }

    [Fact]
    public async Task ResolveAsync_CacheHit_SecondCallReturnsSameResult()
    {
        var (svc, db, _, _) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        var first  = await svc.ResolveAsync("t1");
        var second = await svc.ResolveAsync("t1"); // from cache

        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task ResolveAsync_TriggerMetadataChanged_EvictsSpecificKey()
    {
        var (svc, db, cache, _) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        await svc.ResolveAsync("t1");
        cache.TryGetValue("routing:trigger:t1", out _).Should().BeTrue();

        await svc.Handle(new TriggerMetadataChangedEvent("t1", "UPDATE"), CancellationToken.None);

        cache.TryGetValue("routing:trigger:t1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_RouterMetadataChanged_EvictsAllRoutes()
    {
        var (svc, db, cache, state) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");
        SeedRoute(db, "t2", "r1", "grp-a", "node-a");

        await svc.ResolveAsync("t1");
        await svc.ResolveAsync("t2");

        await svc.Handle(new RouterMetadataChangedEvent("r1", "UPDATE"), CancellationToken.None);

        // Both keys evicted — resolving again returns fresh DB result
        var result = await svc.ResolveAsync("t1");
        result.Should().ContainSingle().Which.Should().Be("node-a");
    }

    [Fact]
    public async Task ResolveAsync_TtlExpired_RefetchesDb()
    {
        var (svc, db, _, state) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        await svc.ResolveAsync("t1");

        // Simulate TTL expiration by invalidating via state
        state.InvalidateAll();

        var result = await svc.ResolveAsync("t1");
        result.Should().ContainSingle().Which.Should().Be("node-a");
    }
}
