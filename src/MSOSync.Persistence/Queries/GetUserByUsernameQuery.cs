using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetUserByUsernameQuery(AppDbContext db)
{
    public Task<SyncUser?> ExecuteAsync(string username, CancellationToken ct = default)
        => db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);
}
