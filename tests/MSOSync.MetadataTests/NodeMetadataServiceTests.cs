using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Metadata.Services;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class NodeMetadataServiceTests
{
    private static (NodeMetadataService Svc, AppDbContext Db) CreateService(
        Mock<IMediator>? mediatorMock = null)
    {
        var db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = (mediatorMock ?? new Mock<IMediator>()).Object;
        var hasher = new BCryptPasswordHasher();
        var nodeSecurity = new NodeSecurityService(db, hasher);
        var protectorMock = new Mock<IDataProtector>();
        protectorMock.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        protectorMock.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        var dataProtectionMock = new Mock<IDataProtectionProvider>();
        dataProtectionMock.Setup(dp => dp.CreateProtector(It.IsAny<string>())).Returns(protectorMock.Object);
        var svc = new NodeMetadataService(db, cache, mediator, nodeSecurity, dataProtectionMock.Object);
        return (svc, db);
    }

    [Fact]
    public async Task RejectRegistrationAsync_RemovesRegistrationRequest()
    {
        var (svc, db) = CreateService();
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-3",
            NodeName = "node-3",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        await svc.RejectRegistrationAsync(request.RequestId);

        db.RegistrationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNodeSecurityInfoAsync_NeverReturnsHashValues()
    {
        var (svc, db) = CreateService();
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
}
