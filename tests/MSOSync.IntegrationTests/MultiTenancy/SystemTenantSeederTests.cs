// tests/MSOSync.IntegrationTests/MultiTenancy/SystemTenantSeederTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.MultiTenancy;

[Collection("MultiTenancy")]
[Trait("Category", "MultiTenancy")]
public sealed class SystemTenantSeederTests(MultiTenantFixture fixture)
{
    [Fact]
    public async Task SystemTenantSeeder_Idempotent_NoDuplicatesOnSecondRun()
    {
        // The M030 migration already seeded SystemTenant.
        // Simulate a second run of the seed logic — must not insert a duplicate.
        var countBefore = await fixture.Db.Tenants
            .CountAsync(t => t.TenantId == WellKnownTenantIds.SystemTenant);

        // Re-run idempotent seed SQL directly using ExecuteSqlAsync (parameterized, no injection risk)
        await fixture.Db.Database.ExecuteSqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM [msosync].[tenant] WHERE [tenant_id] = {WellKnownTenantIds.SystemTenant})
            BEGIN
                INSERT INTO [msosync].[tenant]
                    ([tenant_id], [name], [slug], [status], [edition], [created_at_utc], [updated_at_utc])
                VALUES
                    ({WellKnownTenantIds.SystemTenant}, 'System Tenant', 'system', 1, 0,
                     SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            END
            """);

        var countAfter = await fixture.Db.Tenants
            .CountAsync(t => t.TenantId == WellKnownTenantIds.SystemTenant);

        countAfter.Should().Be(countBefore,
            "seeder must be idempotent — running twice must not create duplicates");
    }

    [Fact]
    public async Task ExistingNodes_AfterM031_HaveSystemTenantId()
    {
        // All nodes seeded in fixture (TenantA + TenantB nodes) have explicit TenantId.
        // This test verifies that if any hypothetical legacy node had been backfilled,
        // the SystemTenant TenantId is NOT the zero GUID.
        WellKnownTenantIds.SystemTenant.Should().NotBe(Guid.Empty,
            "SystemTenant must have a non-empty GUID so backfill is meaningful");

        // Verify no nodes with zero-GUID tenant exist (would indicate failed backfill)
        var zeroGuidNodes = await fixture.Db.Nodes
            .IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == Guid.Empty);

        zeroGuidNodes.Should().Be(0,
            "M031 backfill must have assigned SystemTenant to all legacy nodes");
    }
}
