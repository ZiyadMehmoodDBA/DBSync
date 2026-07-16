using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.Tests.Tenancy;

public sealed class HybridLookupServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly HybridLookupService _sut;

    public HybridLookupServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db  = new AppDbContext(opts);
        _sut = new HybridLookupService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAsync_TenantRecordExists_ReturnsTenantValue()
    {
        var tenantId = Guid.NewGuid();
        _db.Parameters.AddRange(
            new SyncParameter { ParameterName = "timeout", TenantId = null,     ParameterValue = "30" },
            new SyncParameter { ParameterName = "timeout", TenantId = tenantId, ParameterValue = "99" });
        await _db.SaveChangesAsync();

        var result = await _sut.GetParameterAsync(tenantId, "timeout", default);

        result.Should().NotBeNull();
        result!.ParameterValue.Should().Be("99");
    }

    [Fact]
    public async Task GetAsync_NoTenantRecord_ReturnsPlatformDefault()
    {
        var tenantId = Guid.NewGuid();
        _db.Parameters.Add(new SyncParameter { ParameterName = "timeout", TenantId = null, ParameterValue = "30" });
        await _db.SaveChangesAsync();

        var result = await _sut.GetParameterAsync(tenantId, "timeout", default);

        result.Should().NotBeNull();
        result!.ParameterValue.Should().Be("30");
    }

    [Fact]
    public async Task GetAsync_NeitherExists_ReturnsNull()
    {
        var result = await _sut.GetParameterAsync(Guid.NewGuid(), "nonexistent", default);
        result.Should().BeNull();
    }
}
