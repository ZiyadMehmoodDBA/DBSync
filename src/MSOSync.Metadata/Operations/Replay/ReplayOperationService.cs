using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Replay;

public sealed class ReplayOperationService(
    AppDbContext         db,
    IOperationService    operations,
    INodeMetadataService nodeMeta) : IReplayOperationService
{
    private const int MaxRangeDays = 90; // Will be injected from ReplayOptions in production registration
    private const byte BatchStatusError = 3; // BatchStatus.Error — MSOSync.Metadata does not reference MSOSync.Batch

    public async Task<ReplayOperationCreatedDto> CreateAsync(
        CreateReplayRequest req, CancellationToken ct = default)
    {
        // 1. Validate node
        var node = await nodeMeta.GetNodeAsync(req.NodeId, ct);
        if (node is null)
            throw new NotFoundException($"Node '{req.NodeId}' not found", "NODE_NOT_FOUND");

        if (node.LifecycleState is not (NodeLifecycleState.Active or NodeLifecycleState.Draining))
            throw new OperationStateException(
                $"Node '{req.NodeId}' is {node.LifecycleState} — only Active or Draining nodes can be replayed");

        // 2. Validate time range
        if (req.FromTime >= req.ToTime)
            throw new OperationStateException("FromTime must be before ToTime");

        if ((req.ToTime - req.FromTime).TotalDays > MaxRangeDays)
            throw new OperationStateException($"Time range exceeds maximum of {MaxRangeDays} days");

        // 3. Validate BatchIds only for FailedDelivery
        var mode = Enum.Parse<ReplayMode>(req.ReplayMode);
        if (req.BatchIds is { Length: > 0 } && mode != ReplayMode.FailedDelivery)
            throw new OperationStateException("BatchIds can only be specified for FailedDelivery mode");

        // 4. Create SyncOperation
        var operationId = await operations.CreateAsync(
            OperationType.BatchReplay, referenceId: null,
            req.InitiatedBy, OperationSource.User,
            correlationId: Guid.NewGuid().ToString(),
            canCancel: true, canRetry: false,
            summary: $"Batch replay ({req.ReplayMode}) for node {req.NodeId}",
            metadataJson: null, ct);

        // 5. Create SyncReplayRequest
        db.ReplayRequests.Add(new SyncReplayRequest
        {
            ReplayId       = Guid.NewGuid(),
            OperationId    = operationId,
            NodeId         = req.NodeId,
            ChannelIdsJson = req.ChannelIds is null ? null : JsonSerializer.Serialize(req.ChannelIds),
            BatchIdsJson   = req.BatchIds   is null ? null : JsonSerializer.Serialize(req.BatchIds),
            FromTime       = req.FromTime,
            ToTime         = req.ToTime,
            ReplayMode     = req.ReplayMode,
            TenantId       = Guid.Empty, // filled by tenant filter
        });

        // 6. Enumerate items
        var items = await EnumerateItemsAsync(req, mode, operationId, ct);
        foreach (var item in items)
            db.ReplayItems.Add(item);

        await db.SaveChangesAsync(ct);

        // 7. Zero items → complete immediately with NoData
        if (items.Count == 0)
        {
            await operations.CompleteAsync(operationId, OperationResult.NoData,
                "No matching batches found", ct);
        }

        return new ReplayOperationCreatedDto(operationId, items.Count);
    }

    public async Task CancelAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await db.Operations.FindAsync([operationId], ct)
            ?? throw new NotFoundException($"Operation {operationId} not found", "NOT_FOUND");

        if (op.Status is "Completed" or "Failed" or "Cancelled")
            throw new OperationStateException($"Cannot cancel operation in status {op.Status}");

        // Mark pending items as skipped
        await db.ReplayItems
            .Where(i => i.OperationId == operationId && i.Status == nameof(ReplayItemStatus.Pending))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, nameof(ReplayItemStatus.Skipped)), ct);

        await operations.CancelAsync(operationId, Guid.Empty, ct);
    }

    private async Task<List<SyncReplayItem>> EnumerateItemsAsync(
        CreateReplayRequest req, ReplayMode mode, Guid operationId, CancellationToken ct)
    {
        var items = new List<SyncReplayItem>();

        if (mode is ReplayMode.FailedDelivery or ReplayMode.Both)
        {
            var query = db.OutgoingBatches
                .Where(b => b.NodeId == req.NodeId
                         && b.Status == BatchStatusError
                         && b.CreateTime >= req.FromTime
                         && b.CreateTime <= req.ToTime);

            if (req.ChannelIds is { Length: > 0 })
                query = query.Where(b => req.ChannelIds.Contains(b.ChannelId));

            if (req.BatchIds is { Length: > 0 })
                query = query.Where(b => req.BatchIds.Contains(b.BatchId));

            var batches = await query.AsNoTracking().ToListAsync(ct);

            items.AddRange(batches.Select(b => new SyncReplayItem
            {
                ItemId        = Guid.NewGuid(),
                OperationId   = operationId,
                SourceBatchId = b.BatchId,
                NodeId        = b.NodeId,
                ChannelId     = b.ChannelId,
                EventCount    = 0, // not tracked for FailedDelivery
                Status        = nameof(ReplayItemStatus.Pending),
                TenantId      = Guid.Empty,
            }));
        }

        if (mode is ReplayMode.MissedData or ReplayMode.Both)
        {
            // Query events in range, group by channel
            var eventQuery = db.DataEvents
                .Where(e => e.CreateTime >= req.FromTime && e.CreateTime <= req.ToTime);

            if (req.ChannelIds is { Length: > 0 })
                eventQuery = eventQuery.Where(e => req.ChannelIds.Contains(e.ChannelId));

            var channels = await eventQuery
                .GroupBy(e => e.ChannelId)
                .Select(g => new { ChannelId = g.Key, EventCount = g.Count() })
                .ToListAsync(ct);

            // For MissedData, worker will resolve routing and filter at advance time
            // Items for MissedData have no source_batch_id
            foreach (var ch in channels)
            {
                // Skip channels already enumerated in FailedDelivery
                if (mode == ReplayMode.Both && items.Any(i => i.ChannelId == ch.ChannelId))
                    continue;

                items.Add(new SyncReplayItem
                {
                    ItemId        = Guid.NewGuid(),
                    OperationId   = operationId,
                    SourceBatchId = null,
                    NodeId        = req.NodeId,
                    ChannelId     = ch.ChannelId,
                    EventCount    = ch.EventCount,
                    Status        = nameof(ReplayItemStatus.Pending),
                    TenantId      = Guid.Empty,
                });
            }
        }

        return items;
    }
}
