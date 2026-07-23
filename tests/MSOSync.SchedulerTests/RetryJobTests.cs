using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class RetryJobTests
{
    private readonly Mock<IDistributedLockService> _lockService = new();
    private readonly Mock<IDistributedLock>        _lockHandle  = new();
    private readonly Mock<IWorkerStatusRegistry>   _registry    = new();
    private readonly Mock<IClock>                  _clock       = new();

    private RetryJob BuildJob()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => _lockService.Object);
        services.AddSingleton<IOptions<DistributedLockOptions>>(
            Options.Create(new DistributedLockOptions { DefaultExpiry = TimeSpan.FromSeconds(30) }));
        services.AddScoped(_ => new RetryProcessor(
            new AppDbContext(dbOptions), _clock.Object, NullLogger<RetryProcessor>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new RetryJob(scopeFactory, _registry.Object, NullLogger<RetryJob>.Instance);
    }

    [Fact]
    public async Task RunTick_skips_processor_when_lock_not_acquired()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.RetryEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(x => x.RecordTickStart(nameof(RetryJob), TickTrigger.Scheduled), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_completes_when_no_retry_candidates()
    {
        _lockHandle.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.RetryEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_lockHandle.Object);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_records_failure_when_lock_service_throws()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.RetryEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(
            x => x.RecordTickFailed(nameof(RetryJob), It.IsAny<InvalidOperationException>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Never);
    }
}
