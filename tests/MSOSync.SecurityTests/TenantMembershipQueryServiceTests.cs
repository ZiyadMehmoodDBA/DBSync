using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.SecurityTests;

public sealed class TenantMembershipQueryServiceTests
{
    private static AppDbContext MakeDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private const long UserId = 42L;
    private const long RoleId = 10L;

    private static async Task SeedBase(AppDbContext db, TenantStatus tenantStatus = TenantStatus.Active,
        MemberStatus memberStatus = MemberStatus.Active)
    {
        db.Set<Tenant>().Add(new Tenant
        {
            TenantId = TenantId, Name = "Acme", Slug = "acme",
            Status = tenantStatus, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new SyncUser
        {
            UserId = UserId, Username = "zia", PasswordHash = "x", Enabled = true,
        });
        db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = TenantId, UserId = UserId, RoleId = RoleId,
            Status = memberStatus, JoinedAt = DateTimeOffset.UtcNow, LastAccessedAt = DateTimeOffset.UtcNow,
        });
        db.Roles.Add(new SyncRole { RoleId = RoleId, RoleName = "Admin" });
        db.UserRoles.Add(new SyncUserRole { UserId = UserId, RoleId = RoleId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSwitchTenantContext_returns_context_for_active_membership()
    {
        using var db = MakeDb();
        await SeedBase(db);
        var svc = new TenantMembershipQueryService(db);

        var result = await svc.GetSwitchTenantContextAsync(UserId, TenantId);

        result.Should().NotBeNull();
        result!.Username.Should().Be("zia");
        result.TenantSlug.Should().Be("acme");
        result.Roles.Should().ContainSingle().Which.Should().Be("Admin");
    }

    [Fact]
    public async Task GetSwitchTenantContext_returns_null_when_membership_missing()
    {
        using var db = MakeDb();
        var svc = new TenantMembershipQueryService(db);

        var result = await svc.GetSwitchTenantContextAsync(UserId, TenantId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSwitchTenantContext_returns_null_when_membership_inactive()
    {
        using var db = MakeDb();
        await SeedBase(db, memberStatus: MemberStatus.Suspended);
        var svc = new TenantMembershipQueryService(db);

        var result = await svc.GetSwitchTenantContextAsync(UserId, TenantId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSwitchTenantContext_returns_null_when_tenant_inactive()
    {
        using var db = MakeDb();
        await SeedBase(db, tenantStatus: TenantStatus.Suspended);
        var svc = new TenantMembershipQueryService(db);

        var result = await svc.GetSwitchTenantContextAsync(UserId, TenantId);

        result.Should().BeNull();
    }
}
