using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.AppTests;

public sealed class HeartbeatOptionsTests
{
    [Fact]
    public void Default_IntervalSeconds_Is30()
    {
        var opts = new HeartbeatOptions();
        Assert.Equal(30, opts.IntervalSeconds);
    }

    [Fact]
    public void Default_ProbeIntervalSeconds_Is60()
    {
        var opts = new HeartbeatOptions();
        Assert.Equal(60, opts.ProbeIntervalSeconds);
    }

    [Fact]
    public void Section_Constant_Is_Heartbeat()
    {
        Assert.Equal("Heartbeat", HeartbeatOptions.Section);
    }
}
