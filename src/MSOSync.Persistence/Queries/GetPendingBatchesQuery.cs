using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetPendingBatchesQuery(AppDbContext db)
{
    public Task<List<SyncOutgoingBatch>> ExecuteAsync(
        string nodeId, string channelId, CancellationToken ct = default)
        => db.OutgoingBatches
            .AsNoTracking()
            .Where(b => b.NodeId == nodeId
                     && b.ChannelId == channelId
                     && (b.Status == 0 || b.Status == 4))
            .OrderBy(b => b.BatchSequence)
            .ToListAsync(ct);
}
