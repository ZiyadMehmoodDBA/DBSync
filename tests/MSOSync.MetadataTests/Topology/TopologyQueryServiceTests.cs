using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Metadata.Topology;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Topology;

public sealed class TopologyQueryServiceTests
{
    private static TopologyQueryService Make(out Microsoft.EntityFrameworkCore.DbContext db)
    {
        var ctx   = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        db = ctx;
        return new TopologyQueryService(ctx, cache);
    }

    private static SyncNodeGroup Group(string id, string? name = null) =>
        new() { GroupId = id, GroupName = name };

    private static SyncNode Node(string id, string groupId,
        ConnectivityStatus cs = ConnectivityStatus.Reachable) =>
        new()
        {
            NodeId             = id,
            GroupId            = groupId,
            SyncUrl            = "http://localhost",
            Status             = "REGISTERED",
            ConnectivityStatus = cs,
        };

    private static SyncRouter Router(string id, string src, string tgt) =>
        new() { RouterId = id, SourceNodeGroup = src, TargetNodeGroup = tgt, Enabled = true };

    private static SyncTrigger Trigger(string id, string channelId) =>
        new() { TriggerId = id, SourceTable = "dbo.T", ChannelId = channelId };

    private static SyncTriggerRouter TriggerRouter(string triggerId, string routerId) =>
        new() { TriggerId = triggerId, RouterId = routerId, Enabled = true };

    // ── GetTopologyGraphAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetTopologyGraph_ReturnsAllGroups()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1"), Group("g2"));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTopologyGraph_NodeIds_HaveGroupPrefix()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1", "Hub"));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);
        var node   = result.Nodes.Single();

        node.Id.Should().Be("group:g1");
        node.GroupId.Should().Be("g1");
        node.Label.Should().Be("Hub");
    }

    [Fact]
    public async Task GetTopologyGraph_AggregatesStatus()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Reachable),
            Node("n2", "g1", ConnectivityStatus.Unreachable),
            Node("n3", "g1", ConnectivityStatus.Degraded));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);
        var node   = result.Nodes.Single();

        node.MemberCount.Should().Be(3);
        node.Status.Should().Be(ConnectivityStatus.Unreachable);  // worst-of-members
        node.TriggerCount.Should().Be(0);
        node.ChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTopologyGraph_WorstOfMembers_UnreachableWins()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Reachable),
            Node("n2", "g1", ConnectivityStatus.Unreachable));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Single().Status.Should().Be(ConnectivityStatus.Unreachable);
    }

    [Fact]
    public async Task GetTopologyGraph_WorstOfMembers_DegradedBeatsUnknown()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Unknown),
            Node("n2", "g1", ConnectivityStatus.Degraded));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Single().Status.Should().Be(ConnectivityStatus.Degraded);
    }

    [Fact]
    public async Task GetTopologyGraph_EmptyGroup_IsUnknown()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Single().Status.Should().Be(ConnectivityStatus.Unknown);
        result.Nodes.Single().MemberCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTopologyGraph_EdgeIds_HaveRouterPrefix()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1"), Group("g2"));
        db.Set<SyncRouter>().Add(Router("r1", "g1", "g2"));
        db.Set<SyncTrigger>().AddRange(
            Trigger("t1", "ch-default"),
            Trigger("t2", "ch-config"));
        db.Set<SyncTriggerRouter>().AddRange(
            TriggerRouter("t1", "r1"),
            TriggerRouter("t2", "r1"));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        var edge = result.Edges.Single();
        edge.Id.Should().Be("router:r1");
        edge.Source.Should().Be("group:g1");
        edge.Target.Should().Be("group:g2");
        edge.IsEnabled.Should().BeTrue();
        edge.ChannelIds.Should().BeEquivalentTo(new[] { "ch-default", "ch-config" });
    }

    [Fact]
    public async Task GetTopologyGraph_TriggerAndChannelCounts_PerSourceGroup()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1"), Group("g2"));
        db.Set<SyncRouter>().Add(Router("r1", "g1", "g2"));
        db.Set<SyncTrigger>().AddRange(
            Trigger("t1", "ch-default"),
            Trigger("t2", "ch-config"));
        db.Set<SyncTriggerRouter>().AddRange(
            TriggerRouter("t1", "r1"),
            TriggerRouter("t2", "r1"));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        var g1 = result.Nodes.Single(n => n.GroupId == "g1");
        g1.TriggerCount.Should().Be(2);
        g1.ChannelCount.Should().Be(2);

        var g2 = result.Nodes.Single(n => n.GroupId == "g2");
        g2.TriggerCount.Should().Be(0);
        g2.ChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTopologyGraph_Meta_HasCorrectCounts()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1"), Group("g2"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Reachable),
            Node("n2", "g2", ConnectivityStatus.Degraded));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Meta.TotalGroups.Should().Be(2);
        result.Meta.TotalNodes.Should().Be(2);
        result.Meta.OnlineNodes.Should().Be(1);  // only g1 is Reachable
        result.Meta.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── GetGroupsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetGroups_ReturnsAllGroups()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1", "Hub"), Group("g2", "Store"));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupsAsync(default);

        result.Should().HaveCount(2);
        result.Select(g => g.GroupId).Should().Contain(new[] { "g1", "g2" });
    }

    // ── GetGroupAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetGroup_Existing_ReturnsDto()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1", "Hub"));
        db.Set<SyncNode>().Add(Node("n1", "g1", ConnectivityStatus.Reachable));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupAsync("g1", default);

        result.Should().NotBeNull();
        result!.GroupId.Should().Be("g1");
        result.Name.Should().Be("Hub");
        result.TotalNodes.Should().Be(1);
        result.ReachableNodes.Should().Be(1);
    }

    [Fact]
    public async Task GetGroup_Missing_ReturnsNull()
    {
        var svc = Make(out var db);
        await db.SaveChangesAsync();

        var result = await svc.GetGroupAsync("nonexistent", default);

        result.Should().BeNull();
    }

    // ── GetGroupNodesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetGroupNodes_ReturnsMemberNodes()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Reachable),
            Node("n2", "g1", ConnectivityStatus.Degraded));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupNodesAsync("g1", default);

        result.Should().HaveCount(2);
        result.Select(n => n.NodeId).Should().BeEquivalentTo(new[] { "n1", "n2" });
        result.All(n => n.Status == NodeStatus.Registered).Should().BeTrue();
    }

    [Fact]
    public async Task GetGroupNodes_EmptyGroup_ReturnsEmptyList()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupNodesAsync("g1", default);

        result.Should().BeEmpty();
    }
}
