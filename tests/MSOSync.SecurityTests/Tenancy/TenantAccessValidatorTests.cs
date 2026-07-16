using FluentAssertions;
using Moq;
using MSOSync.Persistence.Entities;
using MSOSync.Security.Tenancy;
using Xunit;

namespace MSOSync.SecurityTests.Tenancy;

public sealed class TenantAccessValidatorTests
{
    private static ITenantStore BuildStore(Tenant? tenant, TenantMembership? membership)
    {
        var mock = new Mock<ITenantStore>();
        mock.Setup(s => s.FindTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        mock.Setup(s => s.FindMembershipAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);
        return mock.Object;
    }

    private static Tenant ActiveTenant(Guid id) => new()
    {
        TenantId = id, Name = "T", Slug = "t", Status = TenantStatus.Active,
        Edition = MSOSync.Common.Tenancy.EditionType.Community,
        CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static TenantMembership ActiveMembership(Guid tenantId, long userId) => new()
    {
        TenantId = tenantId, UserId = userId, RoleId = 1L,
        Status = MemberStatus.Active, JoinedAt = DateTimeOffset.UtcNow,
        LastAccessedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task MembershipMissing_Throws403()
    {
        var tenantId = Guid.NewGuid();
        var store    = BuildStore(ActiveTenant(tenantId), membership: null);
        var sut      = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 99, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 403);
    }

    [Fact]
    public async Task MembershipSuspended_Throws403()
    {
        var tenantId   = Guid.NewGuid();
        var membership = ActiveMembership(tenantId, 5L);
        membership.Status = MemberStatus.Suspended;
        var store = BuildStore(ActiveTenant(tenantId), membership);
        var sut   = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 5, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 403);
    }

    [Fact]
    public async Task TenantSuspended_Throws409()
    {
        var tenantId = Guid.NewGuid();
        var tenant   = ActiveTenant(tenantId);
        tenant.Status = TenantStatus.Suspended;
        var store = BuildStore(tenant, ActiveMembership(tenantId, 5L));
        var sut   = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 5, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task TenantProvisioning_Throws409()
    {
        var tenantId = Guid.NewGuid();
        var tenant   = ActiveTenant(tenantId);
        tenant.Status = TenantStatus.Provisioning;
        var store = BuildStore(tenant, ActiveMembership(tenantId, 5L));
        var sut   = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 5, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task TenantDeleted_Throws409()
    {
        var tenantId = Guid.NewGuid();
        var tenant   = ActiveTenant(tenantId);
        tenant.Status = TenantStatus.Deleted;
        var store = BuildStore(tenant, ActiveMembership(tenantId, 5L));
        var sut   = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 5, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task AllValid_ReturnsResult()
    {
        var tenantId = Guid.NewGuid();
        var store    = BuildStore(ActiveTenant(tenantId), ActiveMembership(tenantId, 5L));
        var sut      = new TenantAccessValidator(store);

        var result = await sut.ValidateAsync(tenantId, userId: 5, default);

        result.TenantId.Should().Be(tenantId);
        result.TenantSlug.Should().Be("t");
        result.RoleId.Should().Be(1L);
    }
}
