using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Security;

public sealed class UserService(AppDbContext db)
{
    public Task<SyncUser?> FindByUsernameAsync(string username, CancellationToken ct = default) =>
        db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

    public async Task IncrementFailedAttemptsAsync(SyncUser user, CancellationToken ct = default)
    {
        await db.Users
            .Where(u => u.UserId == user.UserId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(u => u.FailedAttempts, u => u.FailedAttempts + 1), ct);
    }

    public async Task LockUserAsync(SyncUser user, DateTime lockedUntil, CancellationToken ct = default)
    {
        await db.Users
            .Where(u => u.UserId == user.UserId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(u => u.LockedUntil, lockedUntil)
                 .SetProperty(u => u.FailedAttempts, 0), ct);
    }

    public async Task ResetFailedAttemptsAsync(SyncUser user, CancellationToken ct = default)
    {
        await db.Users
            .Where(u => u.UserId == user.UserId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(u => u.FailedAttempts, 0)
                 .SetProperty(u => u.LockedUntil, (DateTime?)null), ct);
    }

    public async Task UpdateLastLoginAsync(SyncUser user, CancellationToken ct = default)
    {
        await db.Users
            .Where(u => u.UserId == user.UserId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(u => u.LastLogin, DateTime.UtcNow), ct);
    }

    public async Task<List<string>> GetRolesAsync(long userId, CancellationToken ct = default)
    {
        return await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles.AsNoTracking(),
                ur => ur.RoleId,
                r => r.RoleId,
                (_, r) => r.RoleName)
            .ToListAsync(ct);
    }
}
