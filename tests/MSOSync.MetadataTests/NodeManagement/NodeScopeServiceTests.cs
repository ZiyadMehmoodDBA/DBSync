using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.NodeManagement;

public sealed class NodeScopeServiceTests
{
    [Fact]
    public async Task GetScopeAsync_ReturnsNull_WhenNoScopeExists()
    {
        await using var db = TestDbContext.Create();
        var svc = new NodeScopeService(db);

        var result = await svc.GetScopeAsync("node-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetScopeAsync_CreatesScope_WhenNoneExists()
    {
        await using var db = TestDbContext.Create();
        var svc = new NodeScopeService(db);
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
        var svc = new NodeScopeService(db);
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
        var svc = new NodeScopeService(db);
        var req = new SetNodeScopeRequest(SyncDirection.Bidirectional, InitialLoadPolicy.FullLoad, ["ch-1"], ["trg-1"], ["rtr-1"]);
        await svc.SetScopeAsync("node-1", req, "admin");

        var result = await svc.GetScopeAsync("node-1");

        Assert.NotNull(result);
        Assert.Equal(["ch-1"], result.ChannelIds);
    }
}
