// tests/MSOSync.IntegrationTests/MultiTenancy/TenantAuthFlowTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence.Entities;
using MSOSync.Security.Tenancy;
using Xunit;

namespace MSOSync.IntegrationTests.MultiTenancy;

[Collection("MultiTenancy")]
[Trait("Category", "MultiTenancy")]
public sealed class TenantAuthFlowTests(MultiTenantFixture fixture)
{
    [Fact]
    public async Task SuspendedTenant_AccessValidation_Returns409()
    {
        // Suspend TenantA
        var tenant = await fixture.Db.Tenants.FirstAsync(t => t.TenantId == fixture.TenantAId);
        tenant.Status         = TenantStatus.Suspended;
        tenant.SuspendedAtUtc = DateTimeOffset.UtcNow;
        await fixture.Db.SaveChangesAsync();

        try
        {
            var store     = new DbContextTenantStore(fixture.Db);
            var validator = new TenantAccessValidator(store);

            var act = () => validator.ValidateAsync(fixture.TenantAId, fixture.UserAId, default);
            await act.Should().ThrowAsync<TenantAccessException>()
                .Where(e => e.StatusCode == 409);
        }
        finally
        {
            // Restore
            tenant.Status         = TenantStatus.Active;
            tenant.SuspendedAtUtc = null;
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ProvisioningTenant_AccessValidation_Returns409()
    {
        var tenant = await fixture.Db.Tenants.FirstAsync(t => t.TenantId == fixture.TenantBId);
        tenant.Status = TenantStatus.Provisioning;
        await fixture.Db.SaveChangesAsync();

        try
        {
            var store     = new DbContextTenantStore(fixture.Db);
            var validator = new TenantAccessValidator(store);

            var act = () => validator.ValidateAsync(fixture.TenantBId, fixture.UserBId, default);
            await act.Should().ThrowAsync<TenantAccessException>()
                .Where(e => e.StatusCode == 409);
        }
        finally
        {
            tenant.Status = TenantStatus.Active;
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CE_SystemTenant_ExistsInDatabase()
    {
        var systemTenant = await fixture.Db.Tenants
            .FirstOrDefaultAsync(t => t.TenantId == WellKnownTenantIds.SystemTenant);

        systemTenant.Should().NotBeNull("M030 migration must seed SystemTenant");
        systemTenant!.Slug.Should().Be("system");
        systemTenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task MembershipMissing_AccessValidation_Returns403()
    {
        var store     = new DbContextTenantStore(fixture.Db);
        var validator = new TenantAccessValidator(store);

        // UserA has no membership in TenantB
        var act = () => validator.ValidateAsync(fixture.TenantBId, fixture.UserAId, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 403);
    }
}
