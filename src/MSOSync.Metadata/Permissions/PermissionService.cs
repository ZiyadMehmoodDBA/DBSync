using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Permissions;

public sealed class PermissionService(AppDbContext db, IMemoryCache cache, IMediator mediator)
    : IPermissionService
{
    private static readonly MemoryCacheEntryOptions CacheOptions =
        new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(60));

    private string CacheKey(string roleName) => $"permissions:{roleName}";

    // ── Queries ──────────────────────────────────────────────────────────────

    public async Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(
        string username, CancellationToken ct = default)
    {
        // Resolve role name for user
        var roleName = await (
            from u in db.Users.AsNoTracking()
            join ur in db.UserRoles.AsNoTracking() on u.UserId equals ur.UserId
            join r  in db.Roles.AsNoTracking()    on ur.RoleId  equals r.RoleId
            where u.Username == username
            select r.RoleName
        ).FirstOrDefaultAsync(ct) ?? "VIEWER";

        // Try cache first
        if (cache.TryGetValue(CacheKey(roleName), out IReadOnlyList<string>? cached) && cached is not null)
            return new EffectivePermissionsDto(roleName, cached, DateTimeOffset.UtcNow);

        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleName == roleName)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(ct);

        cache.Set(CacheKey(roleName), (IReadOnlyList<string>)permissions, CacheOptions);
        return new EffectivePermissionsDto(roleName, permissions, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken ct = default)
    {
        return await db.Permissions.AsNoTracking()
            .OrderBy(p => p.Category).ThenBy(p => p.SortOrder)
            .Select(p => new PermissionDto(p.PermissionKey, p.DisplayName, p.Description,
                                           p.Category, p.SortOrder, p.IsSystem))
            .ToListAsync(ct);
    }

    public async Task<RolePermissionsDto> GetRolePermissionsAsync(
        string roleName, CancellationToken ct = default)
    {
        var userCount = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.RoleId
            where r.RoleName == roleName
            select ur
        ).CountAsync(ct);

        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleName == roleName)
            .Join(db.Permissions.AsNoTracking(), rp => rp.PermissionKey, p => p.PermissionKey,
                  (rp, p) => new PermissionDto(p.PermissionKey, p.DisplayName, p.Description,
                                               p.Category, p.SortOrder, p.IsSystem))
            .OrderBy(p => p.Category).ThenBy(p => p.SortOrder)
            .ToListAsync(ct);

        return new RolePermissionsDto(roleName, userCount, permissions);
    }

    public async Task<IReadOnlyList<RolePermissionsDto>> GetAllRolesAsync(CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking()
            .OrderBy(r => r.RoleName)
            .ToListAsync(ct);

        var result = new List<RolePermissionsDto>(roles.Count);
        foreach (var role in roles)
            result.Add(await GetRolePermissionsAsync(role.RoleName, ct));
        return result;
    }

    // ── Writes ───────────────────────────────────────────────────────────────

    public async Task GrantPermissionAsync(
        string roleName, string permissionKey, CancellationToken ct = default)
    {
        var exists = await db.RolePermissions
            .AnyAsync(rp => rp.RoleName == roleName && rp.PermissionKey == permissionKey, ct);
        if (!exists)
        {
            db.RolePermissions.Add(new SyncRolePermission
                { RoleName = roleName, PermissionKey = permissionKey });
        }
        await WriteAuditAsync("GRANT_PERMISSION", roleName, permissionKey, ct);
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey(roleName));
        await mediator.Publish(new PermissionChangedNotification(roleName, "Grant", DateTimeOffset.UtcNow), ct);
    }

    public async Task RevokePermissionAsync(
        string roleName, string permissionKey, CancellationToken ct = default)
    {
        if (roleName.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) &&
            permissionKey.Equals(SystemPermissions.ManageUsers, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"PERMISSION_PROTECTED: {SystemPermissions.ManageUsers} cannot be revoked from ADMIN.");
        }

        var existing = await db.RolePermissions
            .FirstOrDefaultAsync(rp => rp.RoleName == roleName && rp.PermissionKey == permissionKey, ct);
        if (existing is not null)
            db.RolePermissions.Remove(existing);

        await WriteAuditAsync("REVOKE_PERMISSION", roleName, permissionKey, ct);
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey(roleName));
        await mediator.Publish(new PermissionChangedNotification(roleName, "Revoke", DateTimeOffset.UtcNow), ct);
    }

    public async Task ResetRoleToDefaultsAsync(string roleName, CancellationToken ct = default)
    {
        // Delete all current permissions for role
        var current = await db.RolePermissions
            .Where(rp => rp.RoleName == roleName)
            .ToListAsync(ct);
        db.RolePermissions.RemoveRange(current);

        // Insert defaults
        if (SystemPermissions.Defaults.TryGetValue(roleName, out var defaults))
        {
            foreach (var key in defaults)
                db.RolePermissions.Add(new SyncRolePermission { RoleName = roleName, PermissionKey = key });
        }

        await WriteAuditAsync("RESET_ROLE", roleName, "defaults", ct);
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey(roleName));
        await mediator.Publish(new PermissionChangedNotification(roleName, "Reset", DateTimeOffset.UtcNow), ct);
    }

    public async Task CopyPermissionsFromAsync(
        string targetRole, string sourceRole, CancellationToken ct = default)
    {
        // Load source permissions
        var sourceKeys = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleName == sourceRole)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(ct);

        // Delete target permissions
        var targetCurrent = await db.RolePermissions
            .Where(rp => rp.RoleName == targetRole)
            .ToListAsync(ct);
        db.RolePermissions.RemoveRange(targetCurrent);

        // Insert source permissions onto target — transactional single SaveChanges
        foreach (var key in sourceKeys)
            db.RolePermissions.Add(new SyncRolePermission { RoleName = targetRole, PermissionKey = key });

        await WriteAuditAsync("COPY_PERMISSIONS", targetRole, $"from:{sourceRole}", ct);
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey(targetRole));
        await mediator.Publish(new PermissionChangedNotification(targetRole, "Copy", DateTimeOffset.UtcNow), ct);
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private async Task WriteAuditAsync(
        string actionName, string roleName, string objectName, CancellationToken ct)
    {
        db.Audits.Add(new SyncAudit
        {
            ActionName  = actionName,
            Username    = null,        // permission changes are system-level; controller adds context
            ObjectName  = $"roles/{roleName}|{objectName}",
            CreateTime  = DateTime.UtcNow,
        });
        // Note: caller calls SaveChangesAsync which persists audit + data change together
        await Task.CompletedTask;
    }
}
