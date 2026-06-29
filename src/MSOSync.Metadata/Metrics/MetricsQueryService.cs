using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Metrics;

public sealed class MetricsQueryService(AppDbContext db, IMemoryCache cache)
    : IMetricsQueryService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
    };

    public async Task<MetricsSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("metrics:summary:v1", out MetricsSummaryDto? cached))
            return cached!;

        var cutoff = DateTime.UtcNow.AddHours(-24);

        var nodeStats = await db.Nodes.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total       = g.Count(),
                Reachable   = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Reachable),
                Degraded    = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Degraded),
                Unreachable = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Unreachable),
                Unknown     = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Unknown)
            })
            .FirstOrDefaultAsync(ct);

        var incomingQueue = await db.IncomingBatches.AsNoTracking()
            .CountAsync(b => b.Status == IncomingBatchStatus.New || b.Status == IncomingBatchStatus.Applying, ct);

        var outgoingQueue = await db.OutgoingBatches.AsNoTracking()
            .CountAsync(b => b.Status != 2, ct); // 2 = BatchStatus.Acknowledged

        var processed24h = await db.IncomingBatches.AsNoTracking()
            .CountAsync(b => b.AppliedTime >= cutoff, ct);

        var errors24h = await db.BatchErrors.AsNoTracking()
            .CountAsync(e => e.CreateTime >= cutoff, ct);

        double total      = processed24h + errors24h;
        double errorRate  = total == 0 ? 0.0 : Math.Round(errors24h * 100.0 / total, 2);
        double throughput = Math.Round(processed24h / 1440.0, 2);

        var result = new MetricsSummaryDto(
            nodeStats?.Total       ?? 0,
            nodeStats?.Reachable   ?? 0,
            nodeStats?.Degraded    ?? 0,
            nodeStats?.Unreachable ?? 0,
            nodeStats?.Unknown     ?? 0,
            incomingQueue,
            outgoingQueue,
            processed24h,
            errors24h,
            errorRate,
            throughput,
            DateTime.UtcNow);

        cache.Set("metrics:summary:v1", result, CacheOptions);
        return result;
    }

    public async Task<IReadOnlyList<NodeMetricsDto>> GetNodeMetricsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("metrics:nodes:v1", out IReadOnlyList<NodeMetricsDto>? cached))
            return cached!;

        var cutoff = DateTime.UtcNow.AddHours(-24);

        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.NodeId, n.GroupId, n.ConnectivityStatus, n.LastHeartbeat })
            .ToListAsync(ct);

        // Load to C# so AvgApplyTimeMs calculation avoids complex EF GroupBy translation
        var incomingRaw = await db.IncomingBatches.AsNoTracking()
            .Select(b => new { b.NodeId, b.Status, b.AppliedTime, b.ApplyTimeMs })
            .ToListAsync(ct);

        var incomingByNode = incomingRaw.GroupBy(b => b.NodeId).ToDictionary(
            g => g.Key,
            g =>
            {
                var applied  = g.Where(b => b.AppliedTime >= cutoff).ToList();
                var withTime = applied.Where(b => b.ApplyTimeMs.HasValue)
                                      .Select(b => (double)b.ApplyTimeMs!.Value).ToList();
                return new NodeIncoming(
                    QueueDepth:     (long)g.Count(b => b.Status == IncomingBatchStatus.New
                                                     || b.Status == IncomingBatchStatus.Applying),
                    Processed24h:   applied.Count,
                    AvgApplyTimeMs: withTime.Count > 0 ? (double?)withTime.Average() : null
                );
            });

        var outgoingByNode = await db.OutgoingBatches.AsNoTracking()
            .Where(b => b.Status != 2) // 2 = BatchStatus.Acknowledged
            .GroupBy(b => b.NodeId)
            .Select(g => new { NodeId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.NodeId, x => x.Count, ct);

        // Join BatchErrors → OutgoingBatches to get NodeId, then group by NodeId
        var errorsRaw = await db.BatchErrors.AsNoTracking()
            .Where(e => e.CreateTime >= cutoff)
            .Join(db.OutgoingBatches, e => e.BatchId, b => b.BatchId, (e, b) => b.NodeId)
            .ToListAsync(ct);
        var errorsByNode = errorsRaw.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());

        var result = nodes.Select(n =>
        {
            var hasInc = incomingByNode.TryGetValue(n.NodeId, out var inc);
            return new NodeMetricsDto(
                n.NodeId,
                n.GroupId,
                n.ConnectivityStatus,
                hasInc ? inc!.QueueDepth   : 0L,
                outgoingByNode.TryGetValue(n.NodeId, out var og) ? og : 0L,
                hasInc ? inc!.Processed24h : 0,
                errorsByNode.TryGetValue(n.NodeId, out var err) ? err : 0,
                hasInc ? inc!.AvgApplyTimeMs : null,
                n.LastHeartbeat);
        }).ToList();

        cache.Set("metrics:nodes:v1", (IReadOnlyList<NodeMetricsDto>)result, CacheOptions);
        return result;
    }

    public async Task<IReadOnlyList<ChannelMetricsDto>> GetChannelMetricsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("metrics:channels:v1", out IReadOnlyList<ChannelMetricsDto>? cached))
            return cached!;

        var cutoff = DateTime.UtcNow.AddHours(-24);

        // Load to C# so distinct-NodeId count per channel is safe across EF providers
        var incomingRaw = await db.IncomingBatches.AsNoTracking()
            .Select(b => new { b.ChannelId, b.NodeId, b.AppliedTime })
            .ToListAsync(ct);

        var incoming = incomingRaw.GroupBy(b => b.ChannelId).ToDictionary(
            g => g.Key,
            g =>
            {
                var applied = g.Where(b => b.AppliedTime >= cutoff).ToList();
                return new ChannelIncoming(
                    Processed24h:   (long)applied.Count,
                    ActiveNodes24h: applied.Select(b => b.NodeId).Distinct().Count()
                );
            });

        var outgoing = await db.OutgoingBatches.AsNoTracking()
            .Where(b => b.Status != 2) // 2 = BatchStatus.Acknowledged
            .GroupBy(b => b.ChannelId)
            .Select(g => new { ChannelId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.ChannelId, x => x.Count, ct);

        var events = await db.DataEvents.AsNoTracking()
            .Where(e => !e.IsProcessed)
            .GroupBy(e => e.ChannelId)
            .Select(g => new { ChannelId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.ChannelId, x => x.Count, ct);

        // Join BatchErrors → OutgoingBatches to get ChannelId
        var errorsRaw = await db.BatchErrors.AsNoTracking()
            .Where(e => e.CreateTime >= cutoff)
            .Join(db.OutgoingBatches, e => e.BatchId, b => b.BatchId, (e, b) => b.ChannelId)
            .ToListAsync(ct);
        var errors = errorsRaw.GroupBy(ch => ch).ToDictionary(g => g.Key, g => g.Count());

        var channelIds = new HashSet<string>(
            incoming.Keys.Concat(outgoing.Keys).Concat(events.Keys).Concat(errors.Keys));

        var result = channelIds.Select(ch =>
        {
            var hasInc    = incoming.TryGetValue(ch, out var inc);
            var processed = hasInc ? inc!.Processed24h : 0L;
            return new ChannelMetricsDto(
                ch,
                hasInc ? inc!.ActiveNodes24h : 0,
                events.TryGetValue(ch, out var ev) ? ev : 0L,
                outgoing.TryGetValue(ch, out var og) ? og : 0L,
                processed,
                errors.TryGetValue(ch, out var err) ? err : 0,
                Math.Round(processed / 1440.0, 2));
        })
        .OrderBy(x => x.ChannelId)
        .ToList();

        cache.Set("metrics:channels:v1", (IReadOnlyList<ChannelMetricsDto>)result, CacheOptions);
        return result;
    }

    public async Task<IReadOnlyList<RuntimeMetricsDto>> GetRuntimeMetricsAsync(CancellationToken ct)
    {
        return await db.RuntimeStats.AsNoTracking()
            .OrderByDescending(r => r.CreateTime)
            .Select(r => new RuntimeMetricsDto(
                r.HeapUsed, r.HeapMax, r.ThreadCount,
                r.CpuPercent, r.GcCount, r.GcTimeMs, r.UptimeMs,
                r.CreateTime!.Value))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MonitorMetricDto>> GetMonitorMetricsAsync(
        string? nodeId, string? metricName, CancellationToken ct)
    {
        var q = db.Monitors.AsNoTracking();
        if (nodeId     is not null) q = q.Where(m => m.NodeId     == nodeId);
        if (metricName is not null) q = q.Where(m => m.MetricName == metricName);

        return await q.OrderByDescending(m => m.CreateTime)
            .Select(m => new MonitorMetricDto(
                m.NodeId, m.MetricName, m.MetricValue, m.CreateTime!.Value))
            .ToListAsync(ct);
    }

    // ── Private helper records ───────────────────────────────────────────────

    private sealed record NodeIncoming(long QueueDepth, int Processed24h, double? AvgApplyTimeMs);

    private sealed record ChannelIncoming(long Processed24h, int ActiveNodes24h);
}
