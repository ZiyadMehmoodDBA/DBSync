using Microsoft.EntityFrameworkCore;
using MSOSync.Batch;
using MSOSync.Persistence;

namespace MSOSync.Api.Health;

internal sealed class HealthScoringService(AppDbContext db) : IHealthScoringService
{
    public async Task<IReadOnlyList<NodeHealthScore>> GetScoresAsync(CancellationToken ct = default)
    {
        var nodes = await db.Nodes.AsNoTracking().ToListAsync(ct);
        var now = DateTime.UtcNow;

        // Compute last-sync time per node from outgoing batches (AckTime = successful delivery)
        // and error rate from incoming batches with Error status in the last 24 h.
        var since = now.AddHours(-24);

        var outgoingStats = await db.OutgoingBatches
            .AsNoTracking()
            .Where(b => b.CreateTime >= since)
            .GroupBy(b => b.NodeId)
            .Select(g => new
            {
                NodeId     = g.Key,
                LastAckTime = g.Max(b => b.AckTime),
                Total      = g.Count(),
                Errors     = g.Count(b => b.Status == (byte)BatchStatus.Error),
            })
            .ToListAsync(ct);

        var statsByNode = outgoingStats.ToDictionary(s => s.NodeId);

        return nodes.Select(node =>
        {
            // Connectivity score (40 pts): derived from ConnectivityStatus enum
            var conn = node.ConnectivityStatus == ConnectivityStatus.Reachable ? 40
                     : node.ConnectivityStatus == ConnectivityStatus.Degraded  ? 20
                     : 0;  // Unknown or Unreachable

            // Sync lag score (30 pts): time since last successfully acknowledged outgoing batch
            var lag = 0;
            if (statsByNode.TryGetValue(node.NodeId, out var stats) && stats.LastAckTime.HasValue)
            {
                lag = (now - stats.LastAckTime.Value).TotalMinutes switch
                {
                    < 1  => 30,
                    < 5  => 20,
                    < 30 => 10,
                    _    => 0,
                };
            }

            // Error rate score (20 pts): ratio of failed batches over last 24 h
            var errorRate = 20; // default: no batches = assume healthy
            if (statsByNode.TryGetValue(node.NodeId, out var es) && es.Total > 0)
            {
                var rate = (double)es.Errors / es.Total;
                errorRate = rate switch
                {
                    0      => 20,
                    < 0.01 => 15,
                    < 0.05 => 10,
                    _      => 0,
                };
            }

            // Heartbeat score (10 pts): time since last heartbeat reported by node
            var heartbeat = node.LastHeartbeat is null ? 0
                : (now - node.LastHeartbeat.Value).TotalMinutes switch
                {
                    < 5  => 10,
                    < 30 => 5,
                    _    => 0,
                };

            var score = conn + lag + errorRate + heartbeat;
            return new NodeHealthScore(
                node.NodeId,
                node.NodeName,
                score,
                NodeHealthScore.ComputeGrade(score),
                conn,
                lag,
                errorRate,
                heartbeat,
                now);
        }).ToList();
    }

    public async Task<NodeHealthScore?> GetScoreAsync(string nodeId, CancellationToken ct = default)
    {
        var all = await GetScoresAsync(ct);
        return all.FirstOrDefault(s => s.NodeId == nodeId);
    }

}
