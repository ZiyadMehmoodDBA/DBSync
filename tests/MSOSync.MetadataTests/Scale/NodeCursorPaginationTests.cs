using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Caching;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Services;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests.Scale;

public sealed class CursorTokenStringTests
{
    private static readonly byte[] Key = new byte[32]; // all-zeros dev key

    [Fact]
    public void EncodeString_ThenDecodeString_RoundTrips()
    {
        const string nodeId = "node-abc-123";
        long ticks = DateTime.UtcNow.Ticks;

        var token = CursorToken.EncodeString(nodeId, ticks, Key);
        var (id, decodedTicks) = CursorToken.DecodeString(token, Key);

        id.Should().Be(nodeId);
        decodedTicks.Should().Be(ticks);
    }

    [Fact]
    public void DecodeString_TamperedToken_Throws()
    {
        const string nodeId = "node-abc-123";
        var token = CursorToken.EncodeString(nodeId, 0L, Key);

        // Corrupt last char
        var corrupt = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var act = () => CursorToken.DecodeString(corrupt, Key);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecodeString_GarbageInput_Throws()
    {
        var act = () => CursorToken.DecodeString("not-base64!!!", Key);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EncodeString_ProducesOpaqueToken_NotContainingNodeId()
    {
        const string nodeId = "node-secret-id";
        var token = CursorToken.EncodeString(nodeId, 0L, Key);

        // The raw token must NOT contain the node ID in plain text
        token.Should().NotContain(nodeId);
    }

    [Fact]
    public void CursorSigner_EncodeString_DelegatesToCursorToken()
    {
        var signer = new CursorSigner(new byte[32]);
        const string nodeId = "node-xyz";
        long ticks = 12345L;

        var token = signer.EncodeString(nodeId, ticks);
        var (id, t) = signer.DecodeString(token);

        id.Should().Be(nodeId);
        t.Should().Be(ticks);
    }
}

public sealed class NodeCursorPaginationTests
{
    private static (NodeMetadataService Svc, MSOSync.Persistence.AppDbContext Db) Make()
    {
        var db        = TestDbContext.Create();
        var memCache  = new MemoryCache(new MemoryCacheOptions());
        var cacheOpts = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        ICacheService cache = new InMemoryCacheService(memCache, cacheOpts);
        var mediator  = new Mock<IMediator>().Object;
        var hasher    = new BCryptPasswordHasher();
        var nodeSecurity = new NodeSecurityService(db, hasher);
        var protectorMock = new Mock<IDataProtector>();
        protectorMock.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        protectorMock.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        var dataProtectionMock = new Mock<IDataProtectionProvider>();
        dataProtectionMock.Setup(dp => dp.CreateProtector(It.IsAny<string>())).Returns(protectorMock.Object);
        var cursorSigner = new CursorSigner(new byte[32]);
        var svc = new NodeMetadataService(db, cache, mediator, nodeSecurity, dataProtectionMock.Object, cursorSigner);
        return (svc, db);
    }

    private static SyncNode MakeNode(string id, string groupId = "g1") => new()
    {
        NodeId         = id,
        GroupId        = groupId,
        SyncUrl        = "http://localhost",
        LifecycleState = NodeLifecycleState.Active,
    };

    [Fact]
    public async Task GetNodesCursor_FirstPage_ReturnsCorrectItems()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 10; i++)
            db.Nodes.Add(MakeNode($"node-{i:D3}"));
        await db.SaveChangesAsync();

        var result = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 3, Cursor = null }, default);

        result.Items.Should().HaveCount(3);
        result.HasMore.Should().BeTrue();
        result.NextCursor.Should().NotBeNull();
        // Items ordered by node_id ASC — node-001, node-002, node-003
        result.Items[0].NodeId.Should().Be("node-001");
        result.Items[2].NodeId.Should().Be("node-003");
    }

    [Fact]
    public async Task GetNodesCursor_SubsequentPage_ContinuesFromCursor()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 5; i++)
            db.Nodes.Add(MakeNode($"node-{i:D3}"));
        await db.SaveChangesAsync();

        var page1 = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 2, Cursor = null }, default);
        var page2 = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 2, Cursor = page1.NextCursor }, default);

        page2.Items[0].NodeId.Should().Be("node-003");
        page2.Items[1].NodeId.Should().Be("node-004");
        // Ensure no overlap
        page1.Items.Select(n => n.NodeId)
            .Intersect(page2.Items.Select(n => n.NodeId))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task GetNodesCursor_TamperedCursor_ThrowsArgumentException()
    {
        var (svc, _) = Make();
        var act = async () => await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 5, Cursor = "not-a-valid-cursor" }, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetNodesCursor_ExhaustedPagination_HasMoreFalse()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(MakeNode("node-001"), MakeNode("node-002"));
        await db.SaveChangesAsync();

        var result = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 10, Cursor = null }, default);

        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetNodesWithGate_BelowThreshold_ReturnsFullList()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(MakeNode("node-001"), MakeNode("node-002"));
        await db.SaveChangesAsync();

        var gate = await svc.GetNodesWithGateAsync(threshold: 5, default);

        gate.PaginationRequired.Should().BeFalse();
        gate.Items.Should().NotBeNull().And.HaveCount(2);
        gate.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetNodesWithGate_AboveThreshold_ReturnsPaginationRequired()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 5; i++)
            db.Nodes.Add(MakeNode($"node-{i:D3}"));
        await db.SaveChangesAsync();

        var gate = await svc.GetNodesWithGateAsync(threshold: 3, default);

        gate.PaginationRequired.Should().BeTrue();
        gate.Items.Should().BeNull();
        gate.NextCursor.Should().NotBeNull();
    }
}
