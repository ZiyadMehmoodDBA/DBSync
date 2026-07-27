using FluentAssertions;
using MSOSync.Common;
using Xunit;

namespace MSOSync.Tests;

public sealed class InMemoryMetricsServiceTests
{
    private readonly InMemoryMetricsService _svc = new();

    [Fact]
    public void RecordHistogram_StoresValue_RetrievableViaSnapshot()
    {
        _svc.RecordHistogram("sync.pipeline.fetch_ms", 42.5);
        var snap = _svc.GetSnapshot("sync.pipeline.fetch_ms");
        snap.Should().ContainSingle().Which.Should().BeApproximately(42.5, 0.001);
    }

    [Fact]
    public void RecordHistogram_RingBufferCap_OldestEntryEvicted()
    {
        for (int i = 0; i < 1001; i++)
            _svc.RecordHistogram("test.hist", i);
        var snap = _svc.GetSnapshot("test.hist");
        snap.Should().HaveCount(1000);
        snap[0].Should().BeApproximately(1.0, 0.001); // first entry (0) evicted
    }

    [Fact]
    public void IncrementCounter_AccumulatesCorrectly()
    {
        _svc.IncrementCounter("test.counter");
        _svc.IncrementCounter("test.counter");
        _svc.GetCounterValue("test.counter").Should().Be(2);
    }

    [Fact]
    public void RecordHistogram_UnknownName_ReturnsEmpty()
    {
        _svc.GetSnapshot("does.not.exist").Should().BeEmpty();
    }
}
