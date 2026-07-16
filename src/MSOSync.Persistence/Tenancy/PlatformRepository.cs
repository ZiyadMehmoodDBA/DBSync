using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Tenancy;

// INTERNAL — only platform-admin code may use this.
// The ONLY class permitted to call IgnoreQueryFilters().
// Do not expose via public API; inject IPlatformRepository<T> in callers.
internal interface IPlatformRepository<T> where T : class
{
    IQueryable<T> QueryAll();
}

internal sealed class PlatformRepository<T>(AppDbContext db) : IPlatformRepository<T>
    where T : class
{
    public IQueryable<T> QueryAll()
        => db.Set<T>().IgnoreQueryFilters().AsNoTracking();
}
