using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Cluster.Diagnostics;

public sealed class ClusterDiagnosticsQueryService(AppDbContext db) : IClusterDiagnosticsQueryService
{
    private const double MbFactor    = 1.0 / 1_048_576;
    private const double HoursFactor = 1.0 / 3_600_000;

    public async Task<ClusterDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct)
    {
        var rawStats = await db.Set<SyncRuntimeStats>()
            .AsNoTracking()
            .OrderByDescending(s => s.CreateTime)
            .Take(50)
            .Select(s => new
            {
                s.StatId, s.HeapUsed, s.HeapMax, s.CpuPercent,
                s.ThreadCount, s.GcCount, s.UptimeMs, s.CreateTime,
            })
            .ToListAsync(ct);

        var stats = rawStats.Select(s => new RuntimeStatsDto(
            s.StatId,
            s.HeapUsed    is not null ? s.HeapUsed.Value  * MbFactor    : null,
            s.HeapMax     is not null ? s.HeapMax.Value   * MbFactor    : null,
            s.CpuPercent  is not null ? (double)s.CpuPercent.Value      : null,
            s.ThreadCount,
            s.GcCount,
            s.UptimeMs    is not null ? s.UptimeMs.Value  * HoursFactor : null,
            s.CreateTime ?? DateTime.UtcNow)).ToList();

        // SyncLock is [GlobalEntity] — no tenant filter
        var rawLocks = await db.Set<SyncLock>()
            .AsNoTracking()
            .Where(l => l.LockOwner != null && l.LockTime != null)
            .OrderBy(l => l.LockTime)
            .Select(l => new { l.LockName, l.LockOwner, l.LockTime })
            .ToListAsync(ct);

        var now   = DateTime.UtcNow;
        var locks = rawLocks.Select(l =>
        {
            var age = (now - l.LockTime!.Value).TotalSeconds;
            return new ActiveLockDto(l.LockName, l.LockOwner!, age, age > 300);
        }).ToList();

        var rawOps = await db.Operations
            .AsNoTracking()
            .Where(op => op.Status == "Running" || op.Status == "Pending")
            .OrderBy(op => op.StartedAt)
            .Take(20)
            .Select(op => new
            {
                op.OperationId, op.OperationType, op.Status, op.StartedAt, op.ProgressPercent,
            })
            .ToListAsync(ct);

        var slowOps = rawOps.Select(op => new SlowOperationDto(
            op.OperationId,
            op.OperationType,
            op.Status,
            Math.Round((now - op.StartedAt).TotalMinutes, 2),
            op.ProgressPercent)).ToList();

        return new ClusterDiagnosticsDto(stats, locks, slowOps);
    }
}
