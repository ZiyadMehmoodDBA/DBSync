using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerJobGuardTests
{
    private readonly Mock<ISchedulerLockFactory>    _lockFactory = new();
    private readonly Mock<ISchedulerHealthReporter> _health      = new();

    [Fact]
    public async Task RunAsync_Skips_Work_When_Lock_Is_Null()
    {
        _lockFactory
            .Setup(x => x.TryAcquireAsync("SyncJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISchedulerLock?)null);

        var workCalled = false;
        await SchedulerJobGuard.RunAsync(
            "SyncJob",
            _lockFactory.Object,
            _health.Object,
            NullLogger.Instance,
            _ => { workCalled = true; return Task.CompletedTask; },
            CancellationToken.None);

        workCalled.Should().BeFalse();
        _health.Verify(x => x.RecordStandby("SyncJob"), Times.Once);
        _health.Verify(
            x => x.RecordRunning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_Executes_Work_And_Calls_RecordRunning_Then_RecordIdle()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns("SyncJob");
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1234");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync("SyncJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);

        var workCalled = false;
        await SchedulerJobGuard.RunAsync(
            "SyncJob",
            _lockFactory.Object,
            _health.Object,
            NullLogger.Instance,
            _ => { workCalled = true; return Task.CompletedTask; },
            CancellationToken.None);

        workCalled.Should().BeTrue();
        _health.Verify(
            x => x.RecordRunning("SyncJob", "HOST:1234", It.IsAny<DateTimeOffset>()),
            Times.Once);
        _health.Verify(x => x.RecordIdle("SyncJob"), Times.Once);
        _health.Verify(x => x.RecordStandby(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Calls_RecordIdle_Even_When_Work_Throws()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns("PurgeJob");
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1234");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync("PurgeJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);

        var act = async () => await SchedulerJobGuard.RunAsync(
            "PurgeJob",
            _lockFactory.Object,
            _health.Object,
            NullLogger.Instance,
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _health.Verify(x => x.RecordIdle("PurgeJob"), Times.Once);
    }
}
