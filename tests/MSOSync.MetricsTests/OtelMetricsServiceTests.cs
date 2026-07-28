// tests/MSOSync.MetricsTests/OtelMetricsServiceTests.cs
using System.Diagnostics.Metrics;
using FluentAssertions;
using MSOSync.Metrics;
using Xunit;

namespace MSOSync.MetricsTests;

public sealed class OtelMetricsServiceTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _recorded = [];

    public OtelMetricsServiceTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "MSOSync") listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, value, tags.ToArray())));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, (double)value, tags.ToArray())));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void RecordHistogram_EmitsMeasurement_WithCorrectName()
    {
        var svc = new OtelMetricsService();

        svc.RecordHistogram("sync.pipeline.fetch_ms", 42.5);

        _listener.RecordObservableInstruments();
        _recorded.Should().ContainSingle(r => r.Name == "sync.pipeline.fetch_ms" && r.Value == 42.5);
    }

    [Fact]
    public void IncrementCounter_EmitsMeasurement_WithCorrectName()
    {
        var svc = new OtelMetricsService();

        svc.IncrementCounter("sync.batches.sent");

        _listener.RecordObservableInstruments();
        _recorded.Should().ContainSingle(r => r.Name == "sync.batches.sent" && r.Value == 1.0);
    }

    [Fact]
    public void RecordHistogram_IncludesTags_WhenProvided()
    {
        var svc = new OtelMetricsService();

        svc.RecordHistogram("sync.pipeline.send_ms", 10.0,
            new Dictionary<string, string> { ["node_id"] = "node-1" });

        _listener.RecordObservableInstruments();
        var entry = _recorded.First(r => r.Name == "sync.pipeline.send_ms");
        entry.Tags.Should().Contain(t => t.Key == "node_id" && (string?)t.Value == "node-1");
    }
}
