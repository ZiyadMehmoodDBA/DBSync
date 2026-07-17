using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.Tests.Tenancy;

/// <summary>
/// Verifies EF global query filter semantics for three entities added in 15B.
/// Uses in-memory DB — proves filter logic, not SQL behavior.
/// </summary>
public sealed class DomainTenantFilterVerificationTests : IDisposable
{
    private readonly MutableTenantAccessor _accessor = new();
    private readonly AppDbContext          _db;

    public DomainTenantFilterVerificationTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // Disable service-provider caching so each test gets its own compiled model
            // and the tenant-filter closure captures THIS test's MutableTenantAccessor,
            // not a previous test's accessor that EF would re-use from its model cache.
            .EnableServiceProviderCaching(false)
            .Options;
        _db = new AppDbContext(opts, _accessor);
    }

    public void Dispose() => _db.Dispose();

    // ── SyncUserRefreshToken ───────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_PlatformContext_ReturnsAllTenants()
    {
        // Arrange – two tokens from different tenants
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now     = DateTime.UtcNow;
        _db.UserRefreshTokens.AddRange(
            new SyncUserRefreshToken
            {
                TokenHash       = "hashA",
                TokenLookupHash = "lookA",
                UserId          = 1,
                IssuedAt        = now,
                ExpiresAt       = now.AddDays(7),
                TenantId        = tenantA
            },
            new SyncUserRefreshToken
            {
                TokenHash       = "hashB",
                TokenLookupHash = "lookB",
                UserId          = 2,
                IssuedAt        = now,
                ExpiresAt       = now.AddDays(7),
                TenantId        = tenantB
            });
        await _db.SaveChangesAsync();

        // Act – platform context (accessor.TenantId == null)
        _accessor.SetTenantId(null);
        var count = await _db.UserRefreshTokens.CountAsync();

        // Assert – login endpoint runs in platform context; must see all tokens
        count.Should().Be(2);
    }

    [Fact]
    public async Task RefreshToken_TenantContext_ScopesToTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now     = DateTime.UtcNow;
        _db.UserRefreshTokens.AddRange(
            new SyncUserRefreshToken
            {
                TokenHash       = "hashA",
                TokenLookupHash = "lookA",
                UserId          = 1,
                IssuedAt        = now,
                ExpiresAt       = now.AddDays(7),
                TenantId        = tenantA
            },
            new SyncUserRefreshToken
            {
                TokenHash       = "hashB",
                TokenLookupHash = "lookB",
                UserId          = 2,
                IssuedAt        = now,
                ExpiresAt       = now.AddDays(7),
                TenantId        = tenantB
            });
        await _db.SaveChangesAsync();

        // Act – tenant A refresh request; cannot see tenant B's token
        _accessor.SetTenantId(tenantA);
        var tokens = await _db.UserRefreshTokens.ToListAsync();

        // Assert – refresh with tenant A JWT only sees tenant A tokens
        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantA);
    }

    // ── SyncRuntimeStats ──────────────────────────────────────────────────────

    [Fact]
    public async Task RuntimeStats_TenantContext_ScopesToTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _db.RuntimeStats.AddRange(
            new SyncRuntimeStats { CpuPercent = 20m, TenantId = tenantA },
            new SyncRuntimeStats { CpuPercent = 80m, TenantId = tenantB });
        await _db.SaveChangesAsync();

        // Act – tenant A dashboard request
        _accessor.SetTenantId(tenantA);
        var stats = await _db.RuntimeStats.ToListAsync();

        // Assert – tenant A admin sees only their own nodes' stats
        stats.Should().HaveCount(1);
        stats[0].CpuPercent.Should().Be(20m);
    }

    // ── SyncNotification ──────────────────────────────────────────────────────

    [Fact]
    public async Task Notification_TenantContext_ScopesToTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now     = DateTime.UtcNow;
        _db.Notifications.AddRange(
            new SyncNotification
            {
                EventType       = "NodeOffline",
                Severity        = "Warning",
                Title           = "Alert A",
                Body            = "Node went offline",
                CreatedAt       = now,
                LastOccurredAt  = now,
                TenantId        = tenantA
            },
            new SyncNotification
            {
                EventType       = "NodeOffline",
                Severity        = "Warning",
                Title           = "Alert B",
                Body            = "Node went offline",
                CreatedAt       = now,
                LastOccurredAt  = now,
                TenantId        = tenantB
            });
        await _db.SaveChangesAsync();

        // Act – tenant B request
        _accessor.SetTenantId(tenantB);
        var notifications = await _db.Notifications.ToListAsync();

        // Assert – tenant B sees only their notifications
        notifications.Should().HaveCount(1);
        notifications[0].Title.Should().Be("Alert B");
    }
}

/// <summary>
/// Thread-unsafe accessor for unit tests — only one tenant at a time.
/// Same pattern as MutableTenantAccessor in MultiTenantFixture.cs (15A),
/// redefined locally here as a private class (different project).
/// </summary>
internal sealed class MutableTenantAccessor : ICurrentTenantAccessor
{
    private Guid? _tenantId;
    public Guid? TenantId => _tenantId;
    public void SetTenantId(Guid? tenantId) => _tenantId = tenantId;
}
