using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Security;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Security;

public sealed class AuditChainServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    // Use a fixed, non-empty TenantId so AppDbContext.PopulateTenantIds() does not
    // overwrite the value after hash computation (it only mutates Guid.Empty entries).
    private static readonly Guid TestTenant = new("aaaaaaaa-0000-0000-0000-000000000001");

    public AuditChainServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ComputeHash_IsDeterministic_ForSameInput()
    {
        var svc = new AuditChainService(_db);
        var entry = new SyncAudit
        {
            AuditId    = 1,
            ActionName = "login",
            Username   = "alice",
            CreateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TenantId   = TestTenant
        };

        var hash1 = svc.ComputeHash(null, entry);
        var hash2 = svc.ComputeHash(null, entry);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA-256 hex = 32 bytes * 2 chars
    }

    [Fact]
    public void ComputeHash_DiffersWhenPrevHashDiffers()
    {
        var svc = new AuditChainService(_db);
        var entry = new SyncAudit
        {
            AuditId    = 1,
            ActionName = "login",
            Username   = "alice",
            CreateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TenantId   = TestTenant
        };

        var hashA = svc.ComputeHash(null, entry);
        var hashB = svc.ComputeHash("abc123", entry);

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public async Task VerifyChainAsync_ReturnsValid_ForConsistentChain()
    {
        var svc = new AuditChainService(_db);

        var e1 = new SyncAudit
        {
            AuditId    = 1,
            ActionName = "login",
            Username   = "alice",
            CreateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TenantId   = TestTenant
        };
        e1.PrevHash  = null;
        e1.EntryHash = svc.ComputeHash(null, e1);

        var e2 = new SyncAudit
        {
            AuditId    = 2,
            ActionName = "logout",
            Username   = "alice",
            CreateTime = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc),
            TenantId   = TestTenant
        };
        e2.PrevHash  = e1.EntryHash;
        e2.EntryHash = svc.ComputeHash(e1.EntryHash, e2);

        _db.Audits.AddRange(e1, e2);
        await _db.SaveChangesAsync();

        var (isValid, brokenId) = await svc.VerifyChainAsync();

        isValid.Should().BeTrue();
        brokenId.Should().BeNull();
    }

    [Fact]
    public async Task VerifyChainAsync_ReturnsBrokenId_WhenChainTampered()
    {
        var svc = new AuditChainService(_db);

        var e1 = new SyncAudit
        {
            AuditId    = 1,
            ActionName = "login",
            Username   = "alice",
            CreateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TenantId   = TestTenant
        };
        e1.PrevHash  = null;
        e1.EntryHash = svc.ComputeHash(null, e1);

        var e2 = new SyncAudit
        {
            AuditId    = 2,
            ActionName = "logout",
            Username   = "alice",
            CreateTime = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc),
            TenantId   = TestTenant
        };
        e2.PrevHash  = "tampered-hash"; // wrong prev hash — chain break
        e2.EntryHash = svc.ComputeHash("tampered-hash", e2);

        _db.Audits.AddRange(e1, e2);
        await _db.SaveChangesAsync();

        var (isValid, brokenId) = await svc.VerifyChainAsync();

        isValid.Should().BeFalse();
        brokenId.Should().Be(2L);
    }
}
