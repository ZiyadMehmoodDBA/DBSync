using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.AppTests;

public sealed class SyncOptionsTests
{
    [Fact]
    public void Default_IntervalSeconds_Is30()
    {
        var opts = new SyncOptions();
        Assert.Equal(30, opts.IntervalSeconds);
    }

    [Fact]
    public void Default_PullIntervalSeconds_Is10()
    {
        var opts = new SyncOptions();
        Assert.Equal(10, opts.PullIntervalSeconds);
    }

    [Fact]
    public void Section_Constant_Is_Sync()
    {
        Assert.Equal("Sync", SyncOptions.Section);
    }
}
