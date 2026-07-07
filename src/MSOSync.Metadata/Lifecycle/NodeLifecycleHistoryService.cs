using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed class NodeLifecycleHistoryService(AppDbContext db) : INodeLifecycleHistoryService
{
    public Task WriteTransitionAsync(LifecycleTransitionRecord r, CancellationToken ct = default)
    {
        db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId = r.NodeId,
            FromState = r.FromState,
            ToState = r.ToState,
            Trigger = r.Trigger,
            Reason = r.Reason,
            Actor = r.Actor,
            CorrelationId = r.CorrelationId,
            MetadataJson = r.MetadataJson,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        return Task.CompletedTask;
    }

    public async Task<PagedResult<LifecycleHistoryDto>> GetTimelineAsync(
        string nodeId, LifecycleHistoryFilter f, CancellationToken ct = default)
    {
        var query = db.NodeLifecycleHistories.AsNoTracking().Where(h => h.NodeId == nodeId);
        if (f.From is not null) query = query.Where(h => h.OccurredAt >= f.From);
        if (f.To is not null) query = query.Where(h => h.OccurredAt <= f.To);
        if (f.Trigger is not null) query = query.Where(h => h.Trigger == f.Trigger);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.HistoryId)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)
            .Select(h => new LifecycleHistoryDto(
                h.HistoryId, h.NodeId, h.FromState, h.ToState, h.Trigger,
                h.Reason, h.Actor, h.CorrelationId, h.MetadataJson, h.OccurredAt))
            .ToListAsync(ct);

        return new PagedResult<LifecycleHistoryDto>(items, f.Page, f.PageSize, total);
    }

    public Task<LifecycleHistoryDto?> GetLatestAsync(string nodeId, CancellationToken ct = default)
        => db.NodeLifecycleHistories.AsNoTracking()
            .Where(h => h.NodeId == nodeId)
            .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.HistoryId)
            .Select(h => new LifecycleHistoryDto(
                h.HistoryId, h.NodeId, h.FromState, h.ToState, h.Trigger,
                h.Reason, h.Actor, h.CorrelationId, h.MetadataJson, h.OccurredAt))
            .FirstOrDefaultAsync(ct);

    public async Task<NodeStateDto> GetCurrentStateAsync(string nodeId, CancellationToken ct = default)
    {
        var n = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(x => x.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");

        int? drainPercent = null;
        if (n.LifecycleState == NodeLifecycleState.Decommissioning
            && n.DecommissionInitialOpenBatches is > 0)
        {
            var openNow = await CountOpenBatchesAsync(db, nodeId, ct);
            var initial = n.DecommissionInitialOpenBatches.Value;
            drainPercent = Math.Clamp(100 - (int)Math.Round(openNow * 100.0 / initial), 0, 100);
        }

        return new NodeStateDto(
            n.NodeId, n.LifecycleState, n.ConnectivityStatus, n.ConnectivityReason?.ToString(),
            n.LastHeartbeat is null ? null : new DateTimeOffset(DateTime.SpecifyKind(n.LastHeartbeat.Value, DateTimeKind.Utc)),
            n.LastProbeTime is null ? null : new DateTimeOffset(DateTime.SpecifyKind(n.LastProbeTime.Value, DateTimeKind.Utc)),
            n.MaintenanceMode, n.MaintenanceReason, n.MaintenanceUntil,
            n.LifecycleState == NodeLifecycleState.Decommissioning,
            drainPercent, n.DecommissionGraceUntil);
    }

    /// <summary>
    /// Open = the batch is not yet acknowledged. SyncOutgoingBatch.Status is a byte;
    /// 2 = Acknowledged (terminal success) — the same "unacked" rule MetricsQueryService
    /// already uses (b.Status != 2). Shared by the drain evaluator (Task 3).
    /// </summary>
    internal static Task<int> CountOpenBatchesAsync(AppDbContext db, string nodeId, CancellationToken ct)
        => db.OutgoingBatches.CountAsync(b => b.NodeId == nodeId && b.Status != 2, ct);
}
