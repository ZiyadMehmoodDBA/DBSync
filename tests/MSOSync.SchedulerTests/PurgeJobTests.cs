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
using MSOSync.Event;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class PurgeJobTests
{
    private readonly Mock<IDistributedLockService> _lockService = new();
    private readonly Mock<IDistributedLock>        _lockHandle  = new();
    private readonly Mock<IWorkerStatusRegistry>   _registry    = new();
    private readonly Mock<IEventPurger>            _eventPurger = new();
    private readonly Mock<IClock>                  _clock       = new();

    private PurgeJob BuildJob()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => _lockService.Object);
        services.AddSingleton<IOptions<DistributedLockOptions>>(
            Options.Create(new DistributedLockOptions { DefaultExpiry = TimeSpan.FromSeconds(30) }));
        services.AddScoped(_ => _eventPurger.Object);
        services.AddScoped(_ => new BatchPurger(
            new AppDbContext(dbOptions), _clock.Object, NullLogger<BatchPurger>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new PurgeJob(
            scopeFactory, _clock.Object, _registry.Object, NullLogger<PurgeJob>.Instance);
    }

    [Fact]
    public async Task RunPurge_skips_purgers_when_lock_not_acquired()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.PurgeEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        await BuildJob().RunPurgeAsync(CancellationToken.None);

        _eventPurger.Verify(x => x.PurgeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TimeUntilNextFire_targets_today_when_before_0200_utc()
    {
        _clock.Setup(x => x.UtcNow).Returns(new DateTime(2026, 7, 21, 0, 30, 0, DateTimeKind.Utc));

        var delay = BuildJob().TimeUntilNextFire();

        delay.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void TimeUntilNextFire_targets_tomorrow_when_after_0200_utc()
    {
        _clock.Setup(x => x.UtcNow).Returns(new DateTime(2026, 7, 21, 3, 0, 0, DateTimeKind.Utc));

        var delay = BuildJob().TimeUntilNextFire();

        delay.Should().Be(TimeSpan.FromHours(23));
    }
}
