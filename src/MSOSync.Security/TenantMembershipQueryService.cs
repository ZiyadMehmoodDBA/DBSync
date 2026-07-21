using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Security;

public sealed class TenantMembershipQueryService(AppDbContext db) : ITenantMembershipQueryService
{
    public async Task<SwitchTenantContext?> GetSwitchTenantContextAsync(
        long userId, Guid tenantId, CancellationToken ct = default)
    {
        var membership = await db.TenantMemberships
            .AsNoTracking()
            .Include(m => m.Tenant)
            .FirstOrDefaultAsync(m => m.TenantId == tenantId
                                   && m.UserId   == userId
                                   && m.Status   == MemberStatus.Active, ct);

        if (membership is null || membership.Tenant?.Status != TenantStatus.Active)
            return null;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null) return null;

        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.RoleId, (_, r) => r.RoleName)
            .ToListAsync(ct);

        return new SwitchTenantContext(user.Username, membership.Tenant!.Slug, roles);
    }
}
