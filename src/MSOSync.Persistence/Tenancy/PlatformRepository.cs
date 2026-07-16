using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Tenancy;

// PUBLIC interface — only platform-admin code may inject this.
// The ONLY class permitted to call IgnoreQueryFilters() is PlatformRepository<T> (internal impl).
// Callers outside this assembly must inject IPlatformRepository<T>, not AppDbContext.
public interface IPlatformRepository<T> where T : class
{
    IQueryable<T> QueryAll();
}

internal sealed class PlatformRepository<T>(AppDbContext db) : IPlatformRepository<T>
    where T : class
{
    public IQueryable<T> QueryAll()
        => db.Set<T>().IgnoreQueryFilters().AsNoTracking();
}
