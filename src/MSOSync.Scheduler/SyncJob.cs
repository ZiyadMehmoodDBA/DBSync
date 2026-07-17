using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory  scopeFactory,
    IOptions<SyncOptions> syncOptions,
    IWorkerStatusRegistry registry,
    ILogger<SyncJob>      logger) : BackgroundService
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
            registry.RecordTickStart(nameof(SyncJob));
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
                var engine       = scope.ServiceProvider.GetRequiredService<SyncEngine>();

                await using var lease = await lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
                if (lease == null)
                {
                    logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
                    registry.RecordTickComplete(nameof(SyncJob));
                    continue;
                }

                await engine.RunAsync(ct);
                registry.RecordTickComplete(nameof(SyncJob));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(SyncJob), ex);
                logger.LogError(ex, "SyncJob run failed");
            }
        }
    }
}
