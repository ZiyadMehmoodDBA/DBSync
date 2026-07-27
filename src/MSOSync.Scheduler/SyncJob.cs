using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Workers;
using MSOSync.Engine;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory     scopeFactory,
    IOptions<SyncOptions>    syncOptions,
    ISchedulerLockFactory    lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry    registry,
    ILogger<SyncJob>         logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(nameof(SyncJob), TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunTickAsync(ct);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        registry.RecordTickStart(nameof(SyncJob));
        try
        {
            await SchedulerJobGuard.RunAsync(
                nameof(SyncJob),
                lockFactory,
                health,
                logger,
                async innerCt =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();
                    await engine.RunAsync(innerCt);
                },
                ct);

            registry.RecordTickComplete(nameof(SyncJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(SyncJob), ex);
            logger.LogError(ex, "SyncJob run failed");
        }
    }
}
