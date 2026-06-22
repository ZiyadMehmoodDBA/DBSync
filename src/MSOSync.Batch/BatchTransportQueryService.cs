using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public sealed class BatchTransportQueryService(AppDbContext db) : IBatchTransportQueryService
{
    public async Task<(SyncOutgoingBatch? Batch, bool MoreAvailable)> GetNextPullBatchAsync(
        string targetNodeId, string channelId, long afterSequence, CancellationToken ct = default)
    {
        // Take 2: first is the batch to serve, second (if exists) means MoreAvailable = true
        var candidates = await db.OutgoingBatches
            .Where(b => b.NodeId        == targetNodeId
                     && b.ChannelId     == channelId
                     && b.BatchSequence > afterSequence
                     && (b.Status == (byte)BatchStatus.New || b.Status == (byte)BatchStatus.Retry))
            .OrderBy(b => b.BatchSequence)
            .Take(2)
            .AsNoTracking()
            .ToListAsync(ct);

        if (candidates.Count == 0) return (null, false);
        return (candidates[0], candidates.Count > 1);
    }

    public async Task<IReadOnlyList<SyncDataEvent>> GetEventsForBatchAsync(
        long batchId, CancellationToken ct = default)
    {
        // DataEventBatches links events to outgoing batches
        var eventIds = await db.DataEventBatches
            .Where(deb => deb.BatchId == batchId)
            .AsNoTracking()
            .Select(deb => deb.EventId)
            .ToListAsync(ct);

        if (eventIds.Count == 0) return [];

        return await db.DataEvents
            .Where(e => eventIds.Contains(e.EventId))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<long> GetLastSequenceAsync(
        string sourceNodeId, string channelId, CancellationToken ct = default)
    {
        var max = await db.IncomingBatches
            .Where(b => b.SourceNodeId == sourceNodeId && b.ChannelId == channelId)
            .Select(b => (long?)b.BatchSequence)
            .MaxAsync(ct);

        return max ?? 0L;
    }

    public async Task<bool> IncomingBatchExistsAsync(
        string sourceNodeId, long batchSequence, CancellationToken ct = default)
    {
        return await db.IncomingBatches
            .AnyAsync(b => b.SourceNodeId == sourceNodeId && b.BatchSequence == batchSequence, ct);
    }

    public async Task InsertIncomingBatchAsync(SyncIncomingBatch batch, CancellationToken ct = default)
    {
        db.IncomingBatches.Add(batch);
        await db.SaveChangesAsync(ct);
    }
}
