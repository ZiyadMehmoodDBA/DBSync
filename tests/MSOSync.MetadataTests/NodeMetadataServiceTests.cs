using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Services;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class NodeMetadataServiceTests
{
    private static (NodeMetadataService Svc, AppDbContext Db, BCryptPasswordHasher Hasher) CreateService(
        Mock<IMediator>? mediatorMock = null)
    {
        var db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = (mediatorMock ?? new Mock<IMediator>()).Object;
        var hasher = new BCryptPasswordHasher();
        var nodeSecurity = new NodeSecurityService(db, hasher);
        var svc = new NodeMetadataService(db, cache, mediator, nodeSecurity);
        return (svc, db, hasher);
    }

    [Fact]
    public async Task ApproveRegistrationAsync_ValidRequest_CreatesNodeAndReturnsToken()
    {
        var (svc, db, _) = CreateService();
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-1",
            NodeGroup = "default",
            SyncUrl = "http://node1:8080",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        var result = await svc.ApproveRegistrationAsync(request.RequestId);

        result.NodeId.Should().Be("node-1");
        result.RawToken.Should().NotBeNullOrEmpty();
        db.Nodes.Should().ContainSingle(n => n.NodeId == "node-1");
        db.NodeSecurities.Should().ContainSingle(s => s.NodeId == "node-1");
    }

    [Fact]
    public async Task ApproveRegistrationAsync_TokenVerifies_BCryptHashMatch()
    {
        var (svc, db, hasher) = CreateService();
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-2",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        var result = await svc.ApproveRegistrationAsync(request.RequestId);

        var sec = db.NodeSecurities.Single(s => s.NodeId == "node-2");
        hasher.Verify(result.RawToken, sec.CurrentTokenHash).Should().BeTrue();
    }

    [Fact]
    public async Task RejectRegistrationAsync_RemovesRegistrationRequest()
    {
        var (svc, db, _) = CreateService();
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-3",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        await svc.RejectRegistrationAsync(request.RequestId);

        db.RegistrationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnableNodeAsync_SetsEnabledTrue()
    {
        var (svc, db, _) = CreateService();
        db.Nodes.Add(new SyncNode
        {
            NodeId = "node-4", GroupId = "default",
            SyncUrl = "http://n4", Status = "APPROVED",
            SyncEnabled = false
        });
        await db.SaveChangesAsync();

        await svc.EnableNodeAsync("node-4");

        db.Nodes.Single().SyncEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DisableNodeAsync_SetsEnabledFalse()
    {
        var (svc, db, _) = CreateService();
        db.Nodes.Add(new SyncNode
        {
            NodeId = "node-5", GroupId = "default",
            SyncUrl = "http://n5", Status = "APPROVED",
            SyncEnabled = true
        });
        await db.SaveChangesAsync();

        await svc.DisableNodeAsync("node-5");

        db.Nodes.Single().SyncEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetNodeSecurityInfoAsync_NeverReturnsHashValues()
    {
        var (svc, db, _) = CreateService();
        db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = "node-6",
            CurrentTokenHash = "hashed-value-here",
            CreatedTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await svc.GetNodeSecurityInfoAsync("node-6");

        result.NodeId.Should().Be("node-6");
        result.HasPendingRotation.Should().BeFalse();
    }

    [Fact]
    public async Task ApproveRegistrationAsync_NonExistentRequest_ThrowsNotFoundException()
    {
        var (svc, _, _) = CreateService();

        var act = () => svc.ApproveRegistrationAsync(99999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ApproveRegistrationAsync_PublishesNodeMetadataChangedEvent()
    {
        var mediatorMock = new Mock<IMediator>();
        var (svc, db, _) = CreateService(mediatorMock);
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-7",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        await svc.ApproveRegistrationAsync(request.RequestId);

        mediatorMock.Verify(m => m.Publish(
            It.Is<NodeMetadataChangedEvent>(e => e.NodeId == "node-7" && e.Action == "APPROVED"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
