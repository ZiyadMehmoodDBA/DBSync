using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Cluster.Recovery;

public sealed class RecoveryDashboardQueryService(AppDbContext db) : IRecoveryDashboardQueryService
{
    public async Task<RecoveryDashboardDto> GetRecoveryDashboardAsync(CancellationToken ct)
    {
        // 1. Nodes currently in Recovery lifecycle state
        var recoveryNodeIds = await db.Nodes
            .AsNoTracking()
            .Where(n => n.LifecycleState == NodeLifecycleState.Recovery)
            .Select(n => n.NodeId)
            .ToListAsync(ct);

        // 2. Most recent Recovery transition per active-recovery node
        var recoveryEntries = recoveryNodeIds.Any()
            ? await db.NodeLifecycleHistories
                .AsNoTracking()
                .Where(h => recoveryNodeIds.Contains(h.NodeId) && h.ToState == NodeLifecycleState.Recovery)
                .Select(h => new { h.NodeId, h.OccurredAt })
                .ToListAsync(ct)
            : [];

        var recoveryStartMap = recoveryEntries
            .GroupBy(h => h.NodeId)
            .ToDictionary(g => g.Key, g => g.Max(h => h.OccurredAt));

        // 3. FailureDetectedAt — last connectivity degradation before recovery start
        Dictionary<string, DateTimeOffset?> failureMap = new();
        if (recoveryNodeIds.Any())
        {
            var connHistory = await db.NodeConnectivityHistories
                .AsNoTracking()
                .Where(h => recoveryNodeIds.Contains(h.NodeId)
                         && (h.NewStatus == ConnectivityStatus.Degraded || h.NewStatus == ConnectivityStatus.Unreachable))
                .Select(h => new { h.NodeId, h.OccurredAt })
                .ToListAsync(ct);

            foreach (var nodeId in recoveryNodeIds)
            {
                var nodeStart = recoveryStartMap.TryGetValue(nodeId, out var rs) ? rs : DateTimeOffset.UtcNow;
                failureMap[nodeId] = connHistory
                    .Where(h => h.NodeId == nodeId && h.OccurredAt <= nodeStart)
                    .OrderByDescending(h => h.OccurredAt)
                    .Select(h => (DateTimeOffset?)h.OccurredAt)
                    .FirstOrDefault();
            }
        }

        // 4. Replay operations for recovery nodes (started after recovery began)
        var replayOpInfos = new List<(Guid OpId, string NodeId, string Status, DateTimeOffset StartedAt)>();
        if (recoveryNodeIds.Any())
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var replayOps = await db.Operations
                .AsNoTracking()
                .Join(db.ReplayRequests, o => o.OperationId, r => r.OperationId,
                      (o, r) => new { o.OperationId, r.NodeId, o.Status, o.StartedAt })
                .Where(x => recoveryNodeIds.Contains(x.NodeId) && x.StartedAt >= thirtyDaysAgo)
                .ToListAsync(ct);

            replayOpInfos.AddRange(replayOps.Select(x =>
                (x.OperationId, x.NodeId, x.Status, new DateTimeOffset(x.StartedAt, TimeSpan.Zero))));
        }

        var replayOpIds = replayOpInfos.Select(x => x.OpId).ToList();
        var replayItemCounts = replayOpIds.Any()
            ? await db.ReplayItems
                .AsNoTracking()
                .Where(i => replayOpIds.Contains(i.OperationId))
                .GroupBy(i => i.OperationId)
                .Select(g => new
                {
                    OperationId = g.Key,
                    Total = g.Count(),
                    Done  = g.Count(i => i.Status == "Completed"),
                })
                .ToListAsync(ct)
            : [];

        // 5. Build active recovery list
        var utcNow = DateTimeOffset.UtcNow;
        var activeRecoveries = recoveryNodeIds.Select(nodeId =>
        {
            var recoveryStart = recoveryStartMap.TryGetValue(nodeId, out var rs) ? rs : utcNow;
            var failureAt     = failureMap.TryGetValue(nodeId, out var fa) ? fa : null;
            var elapsed       = (utcNow - recoveryStart).TotalMinutes;

            var nodeReplayOps = replayOpInfos
                .Where(x => x.NodeId == nodeId && x.StartedAt >= recoveryStart)
                .Select(x =>
                {
                    var counts = replayItemCounts.FirstOrDefault(c => c.OperationId == x.OpId);
                    return new ReplayOpRefDto(x.OpId, x.Status, counts?.Done ?? 0, counts?.Total ?? 0);
                })
                .ToList();

            return new ActiveRecoveryDto(
                nodeId,
                failureAt?.UtcDateTime,
                recoveryStart.UtcDateTime,
                Math.Round(elapsed, 2),
                nodeReplayOps);
        }).ToList();

        // 6. Completed recoveries (Recovery → Active in last 30 days)
        var thirtyDaysAgoDO = DateTimeOffset.UtcNow.AddDays(-30);
        var activeTransitions = await db.NodeLifecycleHistories
            .AsNoTracking()
            .Where(h => h.ToState == NodeLifecycleState.Active && h.OccurredAt >= thirtyDaysAgoDO)
            .Select(h => new { h.NodeId, RestoredAt = h.OccurredAt })
            .ToListAsync(ct);

        var recoveredNodeIds = activeTransitions.Select(t => t.NodeId).Distinct().ToList();
        var completionRecoveryEntries = recoveredNodeIds.Any()
            ? await db.NodeLifecycleHistories
                .AsNoTracking()
                .Where(h => recoveredNodeIds.Contains(h.NodeId) && h.ToState == NodeLifecycleState.Recovery)
                .Select(h => new { h.NodeId, RecoveryStartedAt = h.OccurredAt })
                .ToListAsync(ct)
            : [];

        // Connectivity failures for completed recovery nodes
        var completedConnHistory = recoveredNodeIds.Any()
            ? await db.NodeConnectivityHistories
                .AsNoTracking()
                .Where(h => recoveredNodeIds.Contains(h.NodeId)
                         && (h.NewStatus == ConnectivityStatus.Degraded || h.NewStatus == ConnectivityStatus.Unreachable))
                .Select(h => new { h.NodeId, h.OccurredAt })
                .ToListAsync(ct)
            : [];

        var completedRecoveries = new List<CompletedRecoveryDto>();
        foreach (var nodeId in recoveredNodeIds)
        {
            // Most recent restoration for this node
            var latestRestore = activeTransitions
                .Where(t => t.NodeId == nodeId)
                .OrderByDescending(t => t.RestoredAt)
                .FirstOrDefault();
            if (latestRestore is null) continue;

            // Most recent Recovery entry before restoration
            var recoveryEntry = completionRecoveryEntries
                .Where(h => h.NodeId == nodeId && h.RecoveryStartedAt <= latestRestore.RestoredAt)
                .OrderByDescending(h => h.RecoveryStartedAt)
                .FirstOrDefault();
            if (recoveryEntry is null) continue;

            var failureAt = completedConnHistory
                .Where(h => h.NodeId == nodeId && h.OccurredAt <= recoveryEntry.RecoveryStartedAt)
                .OrderByDescending(h => h.OccurredAt)
                .Select(h => (DateTimeOffset?)h.OccurredAt)
                .FirstOrDefault();

            var rtoMinutes = (latestRestore.RestoredAt - recoveryEntry.RecoveryStartedAt).TotalMinutes;

            completedRecoveries.Add(new CompletedRecoveryDto(
                nodeId,
                failureAt?.UtcDateTime,
                recoveryEntry.RecoveryStartedAt.UtcDateTime,
                latestRestore.RestoredAt.UtcDateTime,
                Math.Round(rtoMinutes, 2)));
        }

        completedRecoveries = completedRecoveries
            .OrderByDescending(r => r.RestoredAt)
            .Take(50)
            .ToList();

        // 7. Summary
        var completedLast30 = completedRecoveries.Count;
        double? avgRto = completedRecoveries.Any() ? completedRecoveries.Average(r => r.RtoMinutes) : null;
        double? maxRto = completedRecoveries.Any() ? completedRecoveries.Max(r => r.RtoMinutes)     : null;

        return new RecoveryDashboardDto(
            new RecoverySummaryDto(activeRecoveries.Count, avgRto, maxRto, completedLast30),
            activeRecoveries,
            completedRecoveries);
    }
}
