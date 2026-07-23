using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class RetryJob(
    IServiceScopeFactory  scopeFactory,
    IWorkerStatusRegistry registry,
    ILogger<RetryJob>     logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // 5-minute fixed interval — retry cadence is not a tuneable operational parameter
        registry.Register(nameof(RetryJob), TimeSpan.FromMinutes(5));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunTickAsync(ct);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        registry.RecordTickStart(nameof(RetryJob));
        try
        {
            await using var scope       = scopeFactory.CreateAsyncScope();
            var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();
            var lockOptions = scope.ServiceProvider.GetRequiredService<IOptions<DistributedLockOptions>>();
            var processor   = scope.ServiceProvider.GetRequiredService<RetryProcessor>();

            var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
            await using var handle = await lockService.TryAcquireAsync(
                LockNames.RetryEngine, owner, lockOptions.Value.DefaultExpiry, ct);

            if (handle == null)
            {
                logger.LogDebug("RetryJob: lock held, skipping");
                registry.RecordTickComplete(nameof(RetryJob));
                return;
            }

            var count = await processor.ProcessAsync(ct);
            if (count > 0) logger.LogInformation("RetryJob queued {Count} batches for retry", count);
            registry.RecordTickComplete(nameof(RetryJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(RetryJob), ex);
            logger.LogError(ex, "RetryJob failed");
        }
    }
}
