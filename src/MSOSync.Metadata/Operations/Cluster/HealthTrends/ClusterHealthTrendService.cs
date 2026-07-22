using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Cluster.HealthTrends;

public sealed class ClusterHealthTrendService(AppDbContext db) : IClusterHealthTrendService
{
    public async Task<ClusterHealthTrendDto> GetTrendsAsync(string window, string? nodeId, CancellationToken ct)
    {
        var (windowSpan, bucketSize, bucketCount) = window switch
        {
            "1h"  => (TimeSpan.FromHours(1),  TimeSpan.FromMinutes(5),  12),
            "6h"  => (TimeSpan.FromHours(6),  TimeSpan.FromMinutes(30), 12),
            "24h" => (TimeSpan.FromHours(24), TimeSpan.FromHours(2),    12),
            "7d"  => (TimeSpan.FromDays(7),   TimeSpan.FromHours(12),   14),
            _     => throw new ArgumentException($"Unknown window: {window}", nameof(window))
        };

        var from = DateTimeOffset.UtcNow - windowSpan;

        var query = db.NodeConnectivityHistories
            .AsNoTracking()
            .Where(h => h.OccurredAt >= from);

        if (nodeId is not null)
            query = query.Where(h => h.NodeId == nodeId);

        var history = await query
            .OrderBy(h => h.OccurredAt)
            .Select(h => new { h.NodeId, h.NewStatus, h.OccurredAt })
            .ToListAsync(ct);

        // Group by node, sorted chronologically
        var byNode = history
            .GroupBy(h => h.NodeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.OccurredAt).ToList());

        // Build buckets — all done in memory after fetching time-filtered rows
        var buckets = new List<HealthBucketDto>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = from + bucketSize * i;
            var bucketEnd   = bucketStart + bucketSize;

            int reachable = 0, degraded = 0, unreachable = 0, transitions = 0;

            foreach (var (_, entries) in byNode)
            {
                // Node's state at end of this bucket = most recent entry with OccurredAt < bucketEnd
                var last = entries.LastOrDefault(e => e.OccurredAt < bucketEnd);
                if (last is not null)
                {
                    switch (last.NewStatus)
                    {
                        case ConnectivityStatus.Reachable:   reachable++;   break;
                        case ConnectivityStatus.Degraded:    degraded++;    break;
                        case ConnectivityStatus.Unreachable: unreachable++; break;
                    }
                }
                transitions += entries.Count(e => e.OccurredAt >= bucketStart && e.OccurredAt < bucketEnd);
            }

            buckets.Add(new HealthBucketDto(
                bucketStart.UtcDateTime,
                reachable,
                degraded,
                unreachable,
                reachable + degraded + unreachable,
                transitions));
        }

        // Per-node probe stats
        var nodeStats = byNode.Select(kvp =>
        {
            var entries    = kvp.Value;
            var mostRecent = entries.Last();

            var consecutive = 0;
            foreach (var e in entries.AsEnumerable().Reverse())
            {
                if (e.NewStatus != ConnectivityStatus.Reachable) consecutive++;
                else break;
            }

            var uptimePct = entries.Count > 0
                ? Math.Round((double)entries.Count(e => e.NewStatus == ConnectivityStatus.Reachable) / entries.Count * 100.0, 2)
                : 100.0;

            return new NodeProbeStatsDto(
                kvp.Key,
                mostRecent.NewStatus.ToString(),
                null,   // No latency field in SyncNodeConnectivityHistory
                consecutive,
                uptimePct);
        }).ToList();

        return new ClusterHealthTrendDto(window, bucketCount, buckets, nodeStats);
    }
}
