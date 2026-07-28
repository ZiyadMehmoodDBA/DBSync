// tests/MSOSync.MetricsTests/MetricsEndpointTests.cs
using FluentAssertions;
using Xunit;

namespace MSOSync.MetricsTests;

public sealed class MetricsEndpointTests
{
    [Fact]
    public void OtelMetricsService_DoesNotThrow_OnConcurrentCalls()
    {
        var svc = new MSOSync.Metrics.OtelMetricsService();

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            svc.IncrementCounter("test.counter");
            svc.RecordHistogram("test.histogram", i * 1.5);
        }));

        var act = () => Task.WhenAll(tasks).GetAwaiter().GetResult();

        act.Should().NotThrow();
    }
}
