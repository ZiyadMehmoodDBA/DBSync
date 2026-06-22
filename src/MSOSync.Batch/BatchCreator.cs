using Microsoft.EntityFrameworkCore;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public sealed class BatchCreator(AppDbContext db, IClock clock) : IBatchCreator
{
    public async Task<IReadOnlyList<SyncOutgoingBatch>> CreateBatchesAsync(
        IReadOnlyList<SyncDataEvent> events,
        IReadOnlyDictionary<long, IReadOnlyList<string>> routes,
        CancellationToken ct = default)
    {
        if (events.Count == 0) return [];

        var channelIds = events.Select(e => e.ChannelId).Distinct().ToList();
        var channels = await db.Channels.AsNoTracking()
            .Where(c => channelIds.Contains(c.ChannelId))
            .ToDictionaryAsync(c => c.ChannelId, ct);

        var maxSeq = await db.OutgoingBatches.AnyAsync(ct)
            ? await db.OutgoingBatches.MaxAsync(b => b.BatchSequence, ct)
            : 0L;

        // Expand events × target nodes
        var pairs = events
            .SelectMany(e => routes.TryGetValue(e.EventId, out var targets)
                ? targets.Select(t => (Event: e, TargetNodeId: t))
                : [])
            .GroupBy(x => (x.Event.ChannelId, x.TargetNodeId));

        var batchBuilds = new List<(SyncOutgoingBatch Batch, List<SyncDataEvent> Events)>();

        foreach (var group in pairs)
        {
            var channelId    = group.Key.ChannelId;
            var targetNodeId = group.Key.TargetNodeId;
            channels.TryGetValue(channelId, out var ch);
            var maxRows  = ch?.MaxBatchToSend ?? 10;
            var maxBytes = ch?.MaxDataSize    ?? 1048576L;

            // Group by transaction, ordered by first event
            var txGroups = group
                .GroupBy(x => x.Event.TransactionId)
                .OrderBy(g => g.Min(x => x.Event.EventId))
                .ToList();

            var currentEvents = new List<SyncDataEvent>();
            var currentBytes  = 0L;

            foreach (var txGroup in txGroups)
            {
                var txEvents = txGroup.Select(x => x.Event).OrderBy(e => e.EventId).ToList();
                var txBytes  = txEvents.Sum(e => (long)(e.RowData?.Length ?? 0) * 2);

                if (currentEvents.Count > 0 &&
                    (currentEvents.Count + txEvents.Count > maxRows ||
                     currentBytes + txBytes > maxBytes))
                {
                    batchBuilds.Add(MakeBuild(++maxSeq, targetNodeId, channelId, currentEvents, clock.UtcNow));
                    currentEvents = [];
                    currentBytes  = 0;
                }

                currentEvents.AddRange(txEvents);
                currentBytes += txBytes;
            }

            if (currentEvents.Count > 0)
                batchBuilds.Add(MakeBuild(++maxSeq, targetNodeId, channelId, currentEvents, clock.UtcNow));
        }

        if (batchBuilds.Count == 0) return [];

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.OutgoingBatches.AddRange(batchBuilds.Select(b => b.Batch));
        await db.SaveChangesAsync(ct); // generates BatchIds

        var links = batchBuilds.SelectMany(b =>
            b.Events.Select(e => new SyncDataEventBatch { EventId = e.EventId, BatchId = b.Batch.BatchId }));
        db.DataEventBatches.AddRange(links);

        var processedIds = batchBuilds.SelectMany(b => b.Events.Select(e => e.EventId)).Distinct().ToList();
        await db.DataEvents.Where(e => processedIds.Contains(e.EventId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsProcessed, true), ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return batchBuilds.Select(b => b.Batch).ToList().AsReadOnly();
    }

    private static (SyncOutgoingBatch Batch, List<SyncDataEvent> Events) MakeBuild(
        long seq, string nodeId, string channelId, List<SyncDataEvent> events, DateTime now)
    {
        var batch = new SyncOutgoingBatch
        {
            BatchSequence = seq,
            NodeId        = nodeId,
            ChannelId     = channelId,
            Status        = (byte)BatchStatus.New,
            RowCount      = events.Count,
            ByteCount     = events.Sum(e => (long)(e.RowData?.Length ?? 0) * 2),
            CreateTime    = now
        };
        return (batch, new List<SyncDataEvent>(events));
    }
}
