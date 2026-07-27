using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Persistence;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class RetryJobTests
{
    private readonly Mock<ISchedulerLockFactory>    _lockFactory = new();
    private readonly Mock<ISchedulerHealthReporter> _health      = new();
    private readonly Mock<IWorkerStatusRegistry>    _registry    = new();
    private readonly Mock<IClock>                   _clock       = new();

    private RetryJob BuildJob()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new RetryProcessor(
            new AppDbContext(dbOptions), _clock.Object, NullLogger<RetryProcessor>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new RetryJob(
            scopeFactory, _lockFactory.Object, _health.Object,
            _registry.Object, NullLogger<RetryJob>.Instance);
    }

    [Fact]
    public async Task RunTick_skips_processor_when_lock_not_acquired()
    {
        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(RetryJob), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISchedulerLock?)null);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(x => x.RecordTickStart(nameof(RetryJob), TickTrigger.Scheduled), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
        _health.Verify(x => x.RecordStandby(nameof(RetryJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_completes_when_no_retry_candidates()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns(nameof(RetryJob));
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(RetryJob), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_records_failure_when_lock_factory_throws()
    {
        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(RetryJob), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(
            x => x.RecordTickFailed(nameof(RetryJob), It.IsAny<InvalidOperationException>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Never);
    }
}
