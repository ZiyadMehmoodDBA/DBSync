using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetRetryCandidatesQuery(AppDbContext db)
{
    public Task<List<SyncOutgoingBatch>> ExecuteAsync(
        int maxRetries, CancellationToken ct = default)
        => db.OutgoingBatches
            .AsNoTracking()
            .Where(b => b.Status == 3
                     && b.RetryCount < maxRetries
                     && b.NextRetryTime <= DateTime.UtcNow)
            .ToListAsync(ct);
}
