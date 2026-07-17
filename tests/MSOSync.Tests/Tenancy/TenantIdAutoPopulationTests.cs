using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.Tests.Tenancy;

public sealed class TenantIdAutoPopulationTests : IDisposable
{
    private readonly MutableTenantAccessor _accessor = new();
    private readonly AppDbContext _db;

    public TenantIdAutoPopulationTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        _db = new AppDbContext(opts, _accessor);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveChanges_TenantContext_SetsEntityTenantId()
    {
        var tenantId = Guid.NewGuid();
        _accessor.SetTenantId(tenantId);

        _db.Audits.Add(new SyncAudit { ActionName = "TEST", Username = "u", CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var audit = await _db.Audits.IgnoreQueryFilters().SingleAsync();
        audit.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task SaveChanges_PlatformContext_UsesSystemTenant()
    {
        _accessor.SetTenantId(null); // platform context

        _db.Audits.Add(new SyncAudit { ActionName = "TEST", Username = "u", CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var audit = await _db.Audits.IgnoreQueryFilters().SingleAsync();
        audit.TenantId.Should().Be(WellKnownTenantIds.SystemTenant);
    }

    [Fact]
    public async Task SaveChanges_ExplicitTenantIdSet_NotOverridden()
    {
        var tenantId = Guid.NewGuid();
        var explicitId = Guid.NewGuid();
        _accessor.SetTenantId(tenantId);

        // Explicitly set a different TenantId (e.g., system tenant for a backfill scenario)
        _db.Audits.Add(new SyncAudit { ActionName = "TEST", Username = "u", CreateTime = DateTime.UtcNow, TenantId = explicitId });
        await _db.SaveChangesAsync();

        var audit = await _db.Audits.IgnoreQueryFilters().SingleAsync();
        audit.TenantId.Should().Be(explicitId, "explicit TenantId must not be overridden");
    }
}
