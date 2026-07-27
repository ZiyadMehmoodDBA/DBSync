using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Locks;
using MSOSync.Scheduler;
using MSOSync.Scheduler.Internal;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerLockFactoryTests
{
    private readonly Mock<IDistributedLockService> _lockService = new();

    private ISchedulerLockFactory BuildFactory(int ttlSeconds = 120, int renewalSeconds = 10)
    {
        var options = Options.Create(new SchedulerLockOptions
        {
            TtlSeconds             = ttlSeconds,
            RenewalIntervalSeconds = renewalSeconds,
            LockPrefix             = "scheduler:"
        });
        return new SchedulerLockFactory(
            _lockService.Object,
            options,
            NullLogger<SchedulerLockFactory>.Instance);
    }

    [Fact]
    public async Task TryAcquireAsync_Returns_Null_When_Lock_Held()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                "scheduler:SyncJob",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        var result = await BuildFactory().TryAcquireAsync("SyncJob", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_Returns_Lock_When_Row_Free()
    {
        var fakeLockHandle = new Mock<IDistributedLock>();
        fakeLockHandle.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockService
            .Setup(x => x.TryAcquireAsync(
                "scheduler:SyncJob",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLockHandle.Object);

        // ReleaseAsync called by DisposeAsync (via SchedulerLockImpl), allow it
        _lockService
            .Setup(x => x.ReleaseAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var result = await BuildFactory().TryAcquireAsync("SyncJob", CancellationToken.None);

        result.Should().NotBeNull();
        result!.JobName.Should().Be("SyncJob");
        result.Owner.Should().MatchRegex(@"^.+:\d+$"); // MachineName:PID format
        result.AcquiredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryAcquireAsync_Prefixes_LockName_With_Scheduler_Colon()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        await BuildFactory().TryAcquireAsync("PullJob", CancellationToken.None);

        _lockService.Verify(
            x => x.TryAcquireAsync(
                "scheduler:PullJob",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
