using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Persistence;

namespace MSOSync.Api.Health;

internal sealed class SloService(AppDbContext db, IOptions<SloOptions> options) : ISloService
{
    public async Task<SloStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var now = DateTime.UtcNow;
        var windowStart = now.AddHours(-opts.WindowHours);

        // Step 1: filter in SQL, project only what we need
        var rawBatches = await db.OutgoingBatches
            .AsNoTracking()
            .Where(b => b.CreateTime >= windowStart && b.AckTime != null)
            .Select(b => new { b.Status, b.CreateTime, b.AckTime })
            .ToListAsync(ct);

        // Step 2: compute in memory (supports InMemory provider used in tests)
        var batches = rawBatches.Select(b => new
        {
            IsSuccess = b.Status == (byte)BatchStatus.Acknowledged,
            DurationMs = (b.AckTime!.Value - b.CreateTime!.Value).TotalMilliseconds,
        }).ToList();

        double deliveryRate;
        if (batches.Count == 0)
        {
            deliveryRate = 1.0; // no data → assume SLO met
        }
        else
        {
            deliveryRate = (double)batches.Count(b => b.IsSuccess) / batches.Count;
        }

        // P99 latency (over successful batches only)
        double p99Ms = 0;
        if (batches.Count > 0)
        {
            var sorted = batches
                .Where(b => b.IsSuccess)
                .Select(b => b.DurationMs)
                .OrderBy(d => d)
                .ToList();

            if (sorted.Count > 0)
            {
                var p99Index = (int)Math.Ceiling(sorted.Count * 0.99) - 1;
                p99Ms = sorted[Math.Max(0, p99Index)];
            }
        }

        return new SloStatus(
            DeliveryRate: deliveryRate,
            DeliveryRateTarget: opts.DeliveryRateTarget,
            DeliveryRateMet: deliveryRate >= opts.DeliveryRateTarget,
            LatencyP99Ms: p99Ms,
            LatencyP99TargetMs: opts.LatencyP99TargetMs,
            LatencyP99Met: p99Ms <= opts.LatencyP99TargetMs,
            WindowStart: windowStart,
            WindowEnd: now);
    }
}
