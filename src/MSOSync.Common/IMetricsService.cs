namespace MSOSync.Common;

/// <summary>
/// Lightweight metrics sink. Records named histograms for pipeline stage timing.
/// Phase 2F will replace InMemoryMetricsService with an OpenTelemetry-backed implementation.
/// </summary>
public interface IMetricsService
{
    /// <summary>Record a duration in milliseconds against a named histogram.</summary>
    void RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null);

    /// <summary>Increment a named counter.</summary>
    void IncrementCounter(string name, IReadOnlyDictionary<string, string>? tags = null);
}
