using FluentAssertions;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerHealthReporterTests
{
    private readonly SchedulerHealthReporter _sut = new();

    [Fact]
    public void GetOne_Returns_Idle_For_Unseen_Job()
    {
        var status = _sut.GetOne("UnknownJob");

        status.Mode.Should().Be(SchedulerJobMode.Idle);
        status.LockOwner.Should().BeNull();
        status.LockedSince.Should().BeNull();
    }

    [Fact]
    public void RecordRunning_Updates_Mode_To_Running_With_Owner()
    {
        var now = DateTimeOffset.UtcNow;
        _sut.RecordRunning("SyncJob", "HOST:1234", now);

        var status = _sut.GetOne("SyncJob");
        status.Mode.Should().Be(SchedulerJobMode.Running);
        status.LockOwner.Should().Be("HOST:1234");
        status.LockedSince.Should().Be(now);
    }

    [Fact]
    public void RecordStandby_Updates_Mode_To_Standby_With_Null_Owner()
    {
        _sut.RecordStandby("PullJob");

        var status = _sut.GetOne("PullJob");
        status.Mode.Should().Be(SchedulerJobMode.Standby);
        status.LockOwner.Should().BeNull();
        status.LockedSince.Should().BeNull();
    }

    [Fact]
    public void RecordIdle_Updates_Mode_To_Idle()
    {
        _sut.RecordRunning("RetryJob", "HOST:99", DateTimeOffset.UtcNow);
        _sut.RecordIdle("RetryJob");

        var status = _sut.GetOne("RetryJob");
        status.Mode.Should().Be(SchedulerJobMode.Idle);
        status.LockOwner.Should().BeNull();
    }

    [Fact]
    public void GetAll_Returns_All_Registered_Jobs()
    {
        _sut.RecordRunning("SyncJob",  "HOST:1", DateTimeOffset.UtcNow);
        _sut.RecordStandby("PullJob");
        _sut.RecordIdle("PurgeJob");

        var all = _sut.GetAll();

        all.Should().HaveCount(3);
        all.Should().ContainSingle(s => s.JobName == "SyncJob"  && s.Mode == SchedulerJobMode.Running);
        all.Should().ContainSingle(s => s.JobName == "PullJob"  && s.Mode == SchedulerJobMode.Standby);
        all.Should().ContainSingle(s => s.JobName == "PurgeJob" && s.Mode == SchedulerJobMode.Idle);
    }

    [Fact]
    public void LastUpdated_Is_Populated_On_Every_Record_Call()
    {
        var before = DateTimeOffset.UtcNow.AddMilliseconds(-10);
        _sut.RecordRunning("SyncJob", "HOST:1", DateTimeOffset.UtcNow);
        var after = DateTimeOffset.UtcNow.AddMilliseconds(10);

        var status = _sut.GetOne("SyncJob");
        status.LastUpdated.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Concurrent_Updates_Do_Not_Throw()
    {
        // Simulate concurrent tick updates from multiple threads
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            if (i % 3 == 0) _sut.RecordRunning("SyncJob", $"HOST:{i}", DateTimeOffset.UtcNow);
            else if (i % 3 == 1) _sut.RecordStandby("SyncJob");
            else _sut.RecordIdle("SyncJob");
        }));

        var act = async () => await Task.WhenAll(tasks);
        act.Should().NotThrowAsync();
    }
}
