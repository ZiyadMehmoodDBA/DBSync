// tests/MSOSync.IntegrationTests/MultiTenancy/HybridEntityTests.cs
using FluentAssertions;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.IntegrationTests.MultiTenancy;

[Collection("MultiTenancy")]
[Trait("Category", "MultiTenancy")]
public sealed class HybridEntityTests(MultiTenantFixture fixture)
{
    [Fact]
    public async Task HybridParameter_TenantOverride_WinsOverPlatform()
    {
        // Seed platform default
        fixture.Db.Parameters.Add(new SyncParameter
        {
            ParameterName  = "hybrid-test-param",
            TenantId       = null,
            ParameterValue = "platform-value",
        });

        // Seed tenant-specific override
        fixture.Db.Parameters.Add(new SyncParameter
        {
            ParameterName  = "hybrid-test-param",
            TenantId       = fixture.TenantAId,
            ParameterValue = "tenant-override",
        });

        await fixture.Db.SaveChangesAsync();

        try
        {
            var svc    = new HybridLookupService(fixture.Db);
            var result = await svc.GetParameterAsync(fixture.TenantAId, "hybrid-test-param", default);

            result.Should().NotBeNull();
            result!.ParameterValue.Should().Be("tenant-override",
                "tenant-specific record must win over platform default");
        }
        finally
        {
            fixture.Db.Parameters.RemoveRange(
                fixture.Db.Parameters.Where(p => p.ParameterName == "hybrid-test-param"));
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task HybridParameter_NoTenantOverride_ReturnsPlatformDefault()
    {
        fixture.Db.Parameters.Add(new SyncParameter
        {
            ParameterName  = "hybrid-platform-only",
            TenantId       = null,
            ParameterValue = "default-30",
        });
        await fixture.Db.SaveChangesAsync();

        try
        {
            var svc    = new HybridLookupService(fixture.Db);
            var result = await svc.GetParameterAsync(fixture.TenantAId, "hybrid-platform-only", default);

            result.Should().NotBeNull();
            result!.ParameterValue.Should().Be("default-30",
                "platform default must be returned when no tenant override exists");
        }
        finally
        {
            fixture.Db.Parameters.RemoveRange(
                fixture.Db.Parameters.Where(p => p.ParameterName == "hybrid-platform-only"));
            await fixture.Db.SaveChangesAsync();
        }
    }
}
