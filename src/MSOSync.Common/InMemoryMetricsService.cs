using System.Collections.Concurrent;

namespace MSOSync.Common;

/// <summary>
/// Thread-safe in-memory histogram and counter store.
/// Each histogram is a ring buffer capped at 1 000 entries to bound memory.
/// Phase 2F replaces this with an OpenTelemetry-backed implementation.
/// </summary>
public sealed class InMemoryMetricsService : IMetricsService
{
    private const int RingBufferCap = 1000;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _histograms = new();
    private readonly ConcurrentDictionary<string, long>                    _counters   = new();

    public void RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null)
    {
        var queue = _histograms.GetOrAdd(name, _ => new ConcurrentQueue<double>());
        queue.Enqueue(valueMs);
        // Evict oldest when over cap
        while (queue.Count > RingBufferCap)
            queue.TryDequeue(out _);
    }

    public void IncrementCounter(string name, IReadOnlyDictionary<string, string>? tags = null)
        => _counters.AddOrUpdate(name, 1L, (_, v) => v + 1);

    /// <summary>Returns a point-in-time snapshot of recorded values (oldest first).</summary>
    public double[] GetSnapshot(string name)
        => _histograms.TryGetValue(name, out var q) ? q.ToArray() : Array.Empty<double>();

    /// <summary>Returns the current counter value (0 if not yet incremented).</summary>
    public long GetCounterValue(string name)
        => _counters.TryGetValue(name, out var v) ? v : 0L;
}
