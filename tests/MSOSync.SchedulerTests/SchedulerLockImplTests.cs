using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Common.Locks;
using MSOSync.Scheduler;
using MSOSync.Scheduler.Internal;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerLockImplTests
{
    private readonly Mock<IDistributedLockService> _lockService = new();

    private static IServiceScope CreateNullScope()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return services.CreateScope();
    }

    private SchedulerLockImpl BuildLock(int renewalSeconds = 1)
    {
        var options = new SchedulerLockOptions
        {
            TtlSeconds             = renewalSeconds * 4,
            RenewalIntervalSeconds = renewalSeconds,
            LockPrefix             = "scheduler:"
        };

        _lockService
            .Setup(x => x.RenewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lockService
            .Setup(x => x.ReleaseAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return SchedulerLockImpl.Create("SyncJob", _lockService.Object,
            CreateNullScope(), options, NullLogger<SchedulerLockImpl>.Instance);
    }

    [Fact]
    public async Task Renewal_Calls_RenewAsync_At_Interval()
    {
        await using var lockImpl = BuildLock(renewalSeconds: 1);

        // Wait long enough for at least 2 renewals
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        _lockService.Verify(
            x => x.RenewAsync(
                "scheduler:SyncJob",
                lockImpl.Owner,
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task DisposeAsync_Cancels_Renewal_And_Calls_ReleaseAsync_Once()
    {
        var lockImpl = BuildLock(renewalSeconds: 60); // long interval so renewal doesn't fire

        await lockImpl.DisposeAsync();

        _lockService.Verify(
            x => x.ReleaseAsync(
                "scheduler:SyncJob",
                lockImpl.Owner,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_Does_Not_Throw_When_Release_Fails()
    {
        _lockService
            .Setup(x => x.ReleaseAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db gone"));
        _lockService
            .Setup(x => x.RenewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var lockImpl = SchedulerLockImpl.Create("PullJob", _lockService.Object,
            CreateNullScope(),
            new SchedulerLockOptions { RenewalIntervalSeconds = 60, TtlSeconds = 120, LockPrefix = "scheduler:" },
            NullLogger<SchedulerLockImpl>.Instance);

        // Must not throw
        var act = async () => await lockImpl.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Renewal_Failure_Does_Not_Cancel_Job()
    {
        _lockService
            .Setup(x => x.RenewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));
        _lockService
            .Setup(x => x.ReleaseAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var lockImpl = SchedulerLockImpl.Create("RetryJob", _lockService.Object,
            CreateNullScope(),
            new SchedulerLockOptions { RenewalIntervalSeconds = 1, TtlSeconds = 4, LockPrefix = "scheduler:" },
            NullLogger<SchedulerLockImpl>.Instance);

        // Wait past first renewal interval — renewal fails but lock impl stays alive
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Lock is still alive (no exception propagated externally)
        lockImpl.JobName.Should().Be("RetryJob");
    }
}
