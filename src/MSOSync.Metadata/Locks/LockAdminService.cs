using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Locks;

public sealed class LockAdminService(AppDbContext db) : ILockAdminService
{
    public async Task<IReadOnlyList<LockDto>> GetLocksAsync(CancellationToken ct = default)
    {
        return await db.Locks.AsNoTracking()
            .OrderBy(l => l.LockName)
            .Select(l => new LockDto(l.LockName, l.LockOwner, l.LockTime))
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteLockAsync(string lockName, CancellationToken ct = default)
    {
        var entity = await db.Locks
            .FirstOrDefaultAsync(l => l.LockName == lockName, ct);

        if (entity is null) return false;

        db.Locks.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
