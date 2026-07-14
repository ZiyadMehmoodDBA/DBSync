using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.NodeManagement;

public sealed class NodeScopeServiceTests
{
    private static NodeScopeService CreateService(AppDbContext db)
    {
        var auditSvc = new Mock<IAuditService>();
        return new NodeScopeService(db, auditSvc.Object);
    }

    private static async Task SeedNodeAsync(AppDbContext db, string nodeId = "node-1")
    {
        db.Nodes.Add(new SyncNode
        {
            NodeId    = nodeId,
            GroupId   = "grp-1",
            SyncUrl   = "https://localhost/sync",
            NodeType  = "Standard",
            ExternalId = nodeId,
            NodeName  = nodeId,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetScopeAsync_ReturnsNull_WhenNoScopeExists()
    {
        await using var db = TestDbContext.Create();
        var svc = CreateService(db);

        var result = await svc.GetScopeAsync("node-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetScopeAsync_ThrowsNotFoundException_WhenNodeDoesNotExist()
    {
        await using var db = TestDbContext.Create();
        var svc = CreateService(db);
        var req = new SetNodeScopeRequest(
            SyncDirection.NodeToHub,
            InitialLoadPolicy.FullLoad,
            ["ch-1"],
            ["trg-1"],
            ["rtr-1"]
        );

        await Assert.ThrowsAsync<NotFoundException>(() => svc.SetScopeAsync("no-such-node", req, "admin"));
    }

    [Fact]
    public async Task SetScopeAsync_CreatesScope_WhenNoneExists()
    {
        await using var db = TestDbContext.Create();
        await SeedNodeAsync(db);
        var svc = CreateService(db);
        var req = new SetNodeScopeRequest(
            SyncDirection.NodeToHub,
            InitialLoadPolicy.FullLoad,
            ["ch-1"],
            ["trg-1"],
            ["rtr-1"]
        );

        var result = await svc.SetScopeAsync("node-1", req, "admin");

        Assert.Equal("node-1", result.NodeId);
        Assert.Equal(SyncDirection.NodeToHub, result.SyncDirection);
        Assert.Equal(InitialLoadPolicy.FullLoad, result.InitialLoadPolicy);
        Assert.Equal(["ch-1"], result.ChannelIds);
        Assert.Equal(["trg-1"], result.TriggerIds);
        Assert.Equal(["rtr-1"], result.RouterIds);
    }

    [Fact]
    public async Task SetScopeAsync_ReplacesScope_WhenAlreadyExists()
    {
        await using var db = TestDbContext.Create();
        await SeedNodeAsync(db);
        var svc = CreateService(db);
        var first = new SetNodeScopeRequest(SyncDirection.HubToNode, InitialLoadPolicy.None, ["ch-1"], [], []);
        await svc.SetScopeAsync("node-1", first, "admin");

        var second = new SetNodeScopeRequest(SyncDirection.Bidirectional, InitialLoadPolicy.ChangesOnly, ["ch-2", "ch-3"], ["trg-1"], ["rtr-1"]);
        var result = await svc.SetScopeAsync("node-1", second, "admin");

        Assert.Equal(SyncDirection.Bidirectional, result.SyncDirection);
        Assert.Equal(["ch-2", "ch-3"], result.ChannelIds);
        Assert.Single(result.TriggerIds);
    }

    [Fact]
    public async Task GetScopeAsync_ReturnsScopeWithAssignments_AfterSet()
    {
        await using var db = TestDbContext.Create();
        await SeedNodeAsync(db);
        var svc = CreateService(db);
        var req = new SetNodeScopeRequest(SyncDirection.Bidirectional, InitialLoadPolicy.FullLoad, ["ch-1"], ["trg-1"], ["rtr-1"]);
        await svc.SetScopeAsync("node-1", req, "admin");

        var result = await svc.GetScopeAsync("node-1");

        Assert.NotNull(result);
        Assert.Equal(["ch-1"], result.ChannelIds);
    }
}
