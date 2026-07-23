using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Lock;
using MSOSync.Routing;
using MSOSync.Scheduler;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SyncJobTests
{
    private readonly Mock<IDistributedLockService>   _lockService = new();
    private readonly Mock<IDistributedLock>           _lockHandle  = new();
    private readonly Mock<IWorkerStatusRegistry>      _registry    = new();
    private readonly Mock<ITriggerDriftDetector>      _driftDetector = new();
    private readonly Mock<IEventReader>               _eventReader   = new();
    private readonly Mock<IRoutingService>            _routing       = new();
    private readonly Mock<IBatchCreator>              _batchCreator  = new();
    private readonly Mock<ITransportService>          _transport     = new();
    private readonly Mock<IMediator>                  _mediator      = new();
    private readonly Mock<IClock>                     _clock         = new();

    private SyncJob BuildJob()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _lockService.Object);
        services.AddSingleton<IOptions<DistributedLockOptions>>(
            Options.Create(new DistributedLockOptions { DefaultExpiry = TimeSpan.FromSeconds(30) }));
        services.AddScoped(_ => new SyncEngine(
            _driftDetector.Object, _eventReader.Object, _routing.Object,
            _batchCreator.Object, _transport.Object, _mediator.Object,
            _clock.Object, NullLogger<SyncEngine>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new SyncJob(
            scopeFactory,
            Options.Create(new SyncOptions()),
            _registry.Object,
            NullLogger<SyncJob>.Instance);
    }

    [Fact]
    public async Task RunTick_skips_engine_when_lock_not_acquired()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.SyncEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(x => x.RecordTickStart(nameof(SyncJob), TickTrigger.Scheduled), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_runs_engine_when_lock_acquired()
    {
        _lockHandle.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.SyncEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_lockHandle.Object);
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SyncDataEvent>());

        await BuildJob().RunTickAsync(CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_records_failure_when_engine_throws()
    {
        _lockHandle.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.SyncEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_lockHandle.Object);
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(
            x => x.RecordTickFailed(nameof(SyncJob), It.IsAny<InvalidOperationException>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Never);
    }
}
