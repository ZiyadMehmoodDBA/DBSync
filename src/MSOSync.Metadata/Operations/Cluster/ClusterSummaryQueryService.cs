using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Cluster;

public sealed class ClusterSummaryQueryService(AppDbContext db) : IClusterSummaryQueryService
{
    public async Task<ClusterSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var nodeStatesTask  = QueryNodeStatesAsync(ct);
        var opCountsTask    = QueryOperationCountsAsync(ct);
        var activeOpsTask   = QueryActiveOperationsAsync(ct);
        var rollingTask     = QueryRollingOperationsAsync(ct);
        var replayTask      = QueryReplayOperationsAsync(ct);
        var lifecycleTask   = QueryRecentNodeChangesAsync(ct);

        await Task.WhenAll(nodeStatesTask, opCountsTask, activeOpsTask,
                           rollingTask, replayTask, lifecycleTask);

        return new ClusterSummaryDto(
            await nodeStatesTask,
            await opCountsTask,
            await activeOpsTask,
            await rollingTask,
            await replayTask,
            await lifecycleTask);
    }

    private async Task<NodeStateCountsDto> QueryNodeStatesAsync(CancellationToken ct)
    {
        var nodes = await db.Nodes
            .AsNoTracking()
            .Select(n => new { n.LifecycleState, n.MaintenanceMode })
            .ToListAsync(ct);

        var total       = nodes.Count;
        var maintenance = nodes.Count(n => n.MaintenanceMode);
        var active      = nodes.Count(n => n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode);
        var draining    = nodes.Count(n => n.LifecycleState == NodeLifecycleState.Draining);
        var offline     = nodes.Count(n =>
            !n.MaintenanceMode &&
            n.LifecycleState != NodeLifecycleState.Active &&
            n.LifecycleState != NodeLifecycleState.Draining);

        return new NodeStateCountsDto(total, active, maintenance, draining, offline);
    }

    private async Task<OperationCountsDto> QueryOperationCountsAsync(CancellationToken ct)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var counts = await db.Operations
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Running        = g.Count(o => o.Status == "Running"),
                Pending        = g.Count(o => o.Status == "Pending"),
                SucceededToday = g.Count(o => o.Status == "Completed"
                    && o.Result == "Success"
                    && o.CompletedAt != null
                    && o.CompletedAt.Value >= todayUtc),
                FailedToday    = g.Count(o => o.Status == "Failed"
                    && o.CompletedAt != null
                    && o.CompletedAt.Value >= todayUtc),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new OperationCountsDto(0, 0, 0, 0)
            : new OperationCountsDto(counts.Running, counts.Pending,
                                     counts.SucceededToday, counts.FailedToday);
    }

    private async Task<IReadOnlyList<ActiveOperationSummaryDto>> QueryActiveOperationsAsync(
        CancellationToken ct)
    {
        return await db.Operations
            .AsNoTracking()
            .Where(o => o.Status == "Running" || o.Status == "Pending")
            .OrderByDescending(o => o.StartedAt)
            .Take(50)
            .Select(o => new ActiveOperationSummaryDto(
                o.OperationId, o.OperationType, o.Status,
                null, // SyncOperation has no NodeId column
                o.ProgressPercent, o.ProgressMessage, o.StartedAt))
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<RollingWaveSummaryDto>> QueryRollingOperationsAsync(
        CancellationToken ct)
    {
        var ops = await db.Operations
            .AsNoTracking()
            .Where(o => (o.OperationType == "RollingMaintenance" || o.OperationType == "RollingUpgrade")
                     && (o.Status == "Running" || o.Status == "Pending"))
            .OrderByDescending(o => o.StartedAt)
            .Take(10)
            .Select(o => new { o.OperationId, o.OperationType, o.Status })
            .ToListAsync(ct);

        if (ops.Count == 0) return Array.Empty<RollingWaveSummaryDto>();

        var opIds = ops.Select(o => o.OperationId).ToList();
        var steps = await db.OperationSteps
            .AsNoTracking()
            .Where(s => opIds.Contains(s.OperationId))
            .Select(s => new { s.OperationId, s.WaveNumber, s.Status })
            .ToListAsync(ct);

        return ops.Select(o =>
        {
            var opSteps = steps.Where(s => s.OperationId == o.OperationId).ToList();
            var done    = opSteps.Count(s => s.Status == "Completed");
            var failed  = opSteps.Count(s => s.Status == "Failed");
            var total   = opSteps.Count;
            var maxWave = opSteps.Count > 0 ? opSteps.Max(s => s.WaveNumber) : 0;
            var curWave = opSteps.Where(s => s.Status == "Running")
                                 .Select(s => s.WaveNumber)
                                 .DefaultIfEmpty(0).Max();

            return new RollingWaveSummaryDto(
                o.OperationId, o.OperationType, o.Status,
                curWave, maxWave, done, total, failed);
        }).ToList().AsReadOnly();
    }

    private async Task<IReadOnlyList<ReplayOperationSummaryDto>> QueryReplayOperationsAsync(
        CancellationToken ct)
    {
        var activeReplayOpIds = await db.Operations
            .AsNoTracking()
            .Where(o => o.OperationType == "BatchReplay"
                     && (o.Status == "Running" || o.Status == "Pending"))
            .OrderByDescending(o => o.StartedAt)
            .Take(10)
            .Select(o => o.OperationId)
            .ToListAsync(ct);

        if (activeReplayOpIds.Count == 0) return Array.Empty<ReplayOperationSummaryDto>();

        var requests = await db.ReplayRequests
            .AsNoTracking()
            .Where(r => activeReplayOpIds.Contains(r.OperationId))
            .Select(r => new { r.OperationId, r.ReplayMode })
            .ToListAsync(ct);

        var ops = await db.Operations
            .AsNoTracking()
            .Where(o => activeReplayOpIds.Contains(o.OperationId))
            .Select(o => new { o.OperationId, o.Status })
            .ToListAsync(ct);

        var itemCounts = await db.ReplayItems
            .AsNoTracking()
            .Where(i => activeReplayOpIds.Contains(i.OperationId))
            .GroupBy(i => i.OperationId)
            .Select(g => new
            {
                OperationId = g.Key,
                Total  = g.Count(),
                Done   = g.Count(i => i.Status == "Completed"),
                Failed = g.Count(i => i.Status == "Failed"),
            })
            .ToListAsync(ct);

        return activeReplayOpIds
            .Select(id =>
            {
                var req    = requests.FirstOrDefault(r => r.OperationId == id);
                var op     = ops.FirstOrDefault(o => o.OperationId == id);
                var counts = itemCounts.FirstOrDefault(c => c.OperationId == id);
                return new ReplayOperationSummaryDto(
                    id,
                    req?.ReplayMode ?? "Unknown",
                    op?.Status ?? "Unknown",
                    counts?.Done ?? 0,
                    counts?.Total ?? 0,
                    counts?.Failed ?? 0);
            })
            .ToList()
            .AsReadOnly();
    }

    private async Task<IReadOnlyList<NodeStateChangeDto>> QueryRecentNodeChangesAsync(
        CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        return await db.NodeLifecycleHistories
            .AsNoTracking()
            .Where(h => h.OccurredAt >= cutoff)
            .OrderByDescending(h => h.OccurredAt)
            .Take(50)
            .Select(h => new NodeStateChangeDto(
                h.NodeId,
                h.FromState == null ? null : h.FromState.ToString(),
                h.ToState.ToString(),
                h.Trigger.ToString(),
                h.OccurredAt))
            .ToListAsync(ct);
    }
}
