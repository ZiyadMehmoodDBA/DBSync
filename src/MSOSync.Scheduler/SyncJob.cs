using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Locks;
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
            await RunTickAsync(ct);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        registry.RecordTickStart(nameof(SyncJob));
        try
        {
            await using var scope       = scopeFactory.CreateAsyncScope();
            var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();
            var lockOptions = scope.ServiceProvider.GetRequiredService<IOptions<DistributedLockOptions>>();
            var engine      = scope.ServiceProvider.GetRequiredService<SyncEngine>();

            var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
            await using var handle = await lockService.TryAcquireAsync(
                LockNames.SyncEngine, owner, lockOptions.Value.DefaultExpiry, ct);

            if (handle == null)
            {
                logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
                registry.RecordTickComplete(nameof(SyncJob));
                return;
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
