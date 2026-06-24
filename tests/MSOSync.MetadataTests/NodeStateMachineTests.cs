using FluentAssertions;
using MSOSync.Metadata.Nodes;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class NodeStateMachineTests
{
    [Fact]
    public async Task TransitionAsync_RegisteredToOffline_Succeeds()
    {
        var db = TestDbContext.Create();
        db.Nodes.Add(new MSOSync.Persistence.Entities.SyncNode
        {
            NodeId = "n1", GroupId = "g", SyncUrl = "http://n1", Status = "REGISTERED"
        });
        await db.SaveChangesAsync();

        var sm = new NodeStateMachine(db);
        await sm.TransitionAsync("n1", "OFFLINE");

        db.ChangeTracker.Clear();
        var node = await db.Nodes.FindAsync("n1");
        node!.Status.Should().Be("OFFLINE");
    }

    [Fact]
    public async Task TransitionAsync_OfflineToRegistered_Succeeds()
    {
        var db = TestDbContext.Create();
        db.Nodes.Add(new MSOSync.Persistence.Entities.SyncNode
        {
            NodeId = "n2", GroupId = "g", SyncUrl = "http://n2", Status = "OFFLINE"
        });
        await db.SaveChangesAsync();

        var sm = new NodeStateMachine(db);
        await sm.TransitionAsync("n2", "REGISTERED");

        db.ChangeTracker.Clear();
        var node = await db.Nodes.FindAsync("n2");
        node!.Status.Should().Be("REGISTERED");
    }

    [Fact]
    public async Task TransitionAsync_DisabledNode_Throws()
    {
        var db = TestDbContext.Create();
        db.Nodes.Add(new MSOSync.Persistence.Entities.SyncNode
        {
            NodeId = "n3", GroupId = "g", SyncUrl = "http://n3", Status = "DISABLED"
        });
        await db.SaveChangesAsync();

        var sm = new NodeStateMachine(db);
        var act = async () => await sm.TransitionAsync("n3", "OFFLINE");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DISABLED*");
    }

    [Fact]
    public async Task TransitionAsync_InvalidTargetStatus_Throws()
    {
        var db = TestDbContext.Create();
        db.Nodes.Add(new MSOSync.Persistence.Entities.SyncNode
        {
            NodeId = "n4", GroupId = "g", SyncUrl = "http://n4", Status = "REGISTERED"
        });
        await db.SaveChangesAsync();

        var sm = new NodeStateMachine(db);
        var act = async () => await sm.TransitionAsync("n4", "DISABLED");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
