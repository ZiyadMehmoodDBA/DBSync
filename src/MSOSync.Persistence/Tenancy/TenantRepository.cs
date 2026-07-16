using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Tenancy;

// Base class for all tenant-scoped repositories.
// Global query filter is active — all queries automatically scoped to current tenant.
// NEVER accept TenantId as a method parameter — tenant always comes from ITenantContext.
public abstract class TenantRepository<T>(AppDbContext db) where T : class, ITenantScoped
{
    protected DbSet<T> Set => db.Set<T>();

    protected Task<T?> FindAsync(object key, CancellationToken ct)
        => db.FindAsync<T>(new[] { key }, ct).AsTask();

    protected Task SaveAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);
}
