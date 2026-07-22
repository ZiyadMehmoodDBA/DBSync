using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Replay;
using MSOSync.Metadata.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;

namespace MSOSync.Scheduler.Workers;

public sealed class ReplayWorker(
    IServiceScopeFactory    scopeFactory,
    IOptions<ReplayOptions> opts,
    IWorkerStatusRegistry   registry,
    ILogger<ReplayWorker>   logger) : BackgroundService
{
    private int _running;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(
            opts.Value.WorkerIntervalSeconds > 0 ? opts.Value.WorkerIntervalSeconds : 10);
        registry.Register(nameof(ReplayWorker), interval);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(
            opts.Value.WorkerIntervalSeconds > 0 ? opts.Value.WorkerIntervalSeconds : 10);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                logger.LogWarning("ReplayWorker tick skipped — previous tick still running");
                continue;
            }
            registry.RecordTickStart(nameof(ReplayWorker));
            try
            {
                await RunTickAsync(ct);
                registry.RecordTickComplete(nameof(ReplayWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(ReplayWorker), ex);
                logger.LogError(ex, "ReplayWorker tick failed");
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db            = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operations    = scope.ServiceProvider.GetRequiredService<IOperationService>();
        var nodeMeta      = scope.ServiceProvider.GetRequiredService<INodeMetadataService>();
        var batchCreator  = scope.ServiceProvider.GetRequiredService<IBatchCreator>();
        var routing       = scope.ServiceProvider.GetRequiredService<IRoutingService>();
        var clock         = scope.ServiceProvider.GetRequiredService<IClock>();
        var maxConcurrent = opts.Value.MaxConcurrentOperations;
        var pageSize      = opts.Value.ItemPageSize;

        // Phase 1 — Advance already-Running operations (captured before any promotions this tick)
        var alreadyRunningIds = await db.Operations
            .Where(o => o.Status == "Running" && o.OperationType == "BatchReplay")
            .Select(o => o.OperationId)
            .ToListAsync(ct);

        foreach (var opId in alreadyRunningIds)
        {
            var op = await db.Operations.FindAsync([opId], ct);
            if (op is null) continue;

            var req = await db.ReplayRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.OperationId == opId, ct);
            if (req is null) continue;

            // Load pending items for this operation
            var pendingItems = await db.ReplayItems
                .Where(i => i.OperationId == opId && i.Status == "Pending")
                .Take(pageSize)
                .ToListAsync(ct);

            if (pendingItems.Count == 0)
            {
                // No pending items — complete the operation
                await CompleteOperationAsync(op, db, operations, ct);
                await db.SaveChangesAsync(ct);
                continue;
            }

            // Check if the target node is still active
            var node       = await nodeMeta.GetNodeAsync(req.NodeId, ct);
            var nodeActive = node is not null
                && node.LifecycleState is NodeLifecycleState.Active;

            foreach (var item in pendingItems)
            {
                if (!nodeActive)
                {
                    item.Status = "Skipped";
                    continue;
                }

                item.Status = "Processing";
                await db.SaveChangesAsync(ct);

                try
                {
                    if (item.SourceBatchId.HasValue)
                    {
                        // FailedDelivery: reset the source batch to Retry so delivery re-runs
                        var batch = await db.OutgoingBatches.FindAsync([item.SourceBatchId.Value], ct);
                        if (batch is not null)
                        {
                            batch.Status = (byte)BatchStatus.Retry;
                        }
                        item.ReplayBatchId = item.SourceBatchId;
                    }
                    else
                    {
                        // MissedData: query events in the channel/time window, resolve routing, create batches
                        var events = await db.DataEvents.AsNoTracking()
                            .Where(e => e.ChannelId == item.ChannelId
                                     && e.CreateTime >= req.FromTime
                                     && e.CreateTime <= req.ToTime)
                            .ToListAsync(ct);

                        if (events.Count > 0)
                        {
                            var routes = new Dictionary<long, IReadOnlyList<string>>();
                            foreach (var ev in events)
                            {
                                var targets = await routing.ResolveAsync(ev.TriggerId, ct);
                                if (targets.Contains(req.NodeId))
                                    routes[ev.EventId] = new[] { req.NodeId };
                            }

                            if (routes.Count > 0)
                            {
                                var batches = await batchCreator.CreateBatchesAsync(events, routes, ct);
                                item.ReplayBatchId = batches.FirstOrDefault()?.BatchId;
                            }
                        }
                    }

                    item.Status = "Completed";
                }
                catch (Exception ex)
                {
                    item.Status       = "Failed";
                    item.ErrorMessage = ex.Message.Length > 1000
                        ? ex.Message[..1000] : ex.Message;
                    logger.LogError(ex, "ReplayWorker failed to process item {ItemId}", item.ItemId);
                }
            }

            // Update progress
            var total     = await db.ReplayItems.CountAsync(i => i.OperationId == opId, ct);
            var completed = await db.ReplayItems.CountAsync(
                i => i.OperationId == opId
                  && (i.Status == "Completed" || i.Status == "Failed" || i.Status == "Skipped"), ct);
            op.ProgressPercent = total > 0 ? completed * 100 / total : 0;

            await db.SaveChangesAsync(ct);

            // Check if now complete (no more pending items)
            var remainingPending = await db.ReplayItems
                .AnyAsync(i => i.OperationId == opId && i.Status == "Pending", ct);
            if (!remainingPending)
            {
                await CompleteOperationAsync(op, db, operations, ct);
                await db.SaveChangesAsync(ct);
            }
        }

        // Phase 2 — Promote Pending operations up to maxConcurrent slots
        var runningCount = await db.Operations
            .CountAsync(o => o.Status == "Running" && o.OperationType == "BatchReplay", ct);

        var slotsAvailable = Math.Max(0, maxConcurrent - runningCount);
        if (slotsAvailable > 0)
        {
            var pendingOps = await db.Operations
                .Where(o => o.Status == "Pending" && o.OperationType == "BatchReplay")
                .Take(slotsAvailable)
                .ToListAsync(ct);

            foreach (var op in pendingOps)
            {
                op.Status    = "Running";
                op.StartedAt = clock.UtcNow;
            }
            if (pendingOps.Count > 0)
                await db.SaveChangesAsync(ct);
        }
    }

    private static async Task CompleteOperationAsync(
        SyncOperation op, AppDbContext db, IOperationService operations, CancellationToken ct)
    {
        var hasFailed = await db.ReplayItems
            .AnyAsync(i => i.OperationId == op.OperationId && i.Status == "Failed", ct);
        var result = hasFailed ? OperationResult.PartialSuccess : OperationResult.Success;
        await operations.CompleteAsync(op.OperationId, result, null, ct);
        op.Status = "Completed";
    }
}
