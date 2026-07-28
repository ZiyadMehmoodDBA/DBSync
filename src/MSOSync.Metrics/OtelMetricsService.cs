// src/MSOSync.Metrics/OtelMetricsService.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MSOSync.Common;

namespace MSOSync.Metrics;

public sealed class OtelMetricsService : IMetricsService
{
    private static readonly Meter _meter = new("MSOSync", "1.0");
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();

    public void IncrementCounter(string name, IReadOnlyDictionary<string, string>? tags = null)
    {
        var counter = _counters.GetOrAdd(name, static n => _meter.CreateCounter<long>(n));
        counter.Add(1, ToTagList(tags));
    }

    public void RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null)
    {
        var histogram = _histograms.GetOrAdd(name, static n => _meter.CreateHistogram<double>(n, unit: "ms"));
        histogram.Record(valueMs, ToTagList(tags));
    }

    private static TagList ToTagList(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null or { Count: 0 }) return default;
        var tagList = new TagList();
        foreach (var (k, v) in tags) tagList.Add(k, v);
        return tagList;
    }
}
