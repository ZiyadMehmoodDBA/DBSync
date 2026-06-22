using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class RetryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RetryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var scope        = scopeFactory.CreateAsyncScope();
            var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
            var processor    = scope.ServiceProvider.GetRequiredService<RetryProcessor>();

            await using var lease = await lockProvider.TryAcquireAsync(LockNames.RetryEngine, ct);
            if (lease == null) { logger.LogDebug("RetryJob: lock held, skipping"); continue; }

            try
            {
                var count = await processor.ProcessAsync(ct);
                if (count > 0) logger.LogInformation("RetryJob queued {Count} batches for retry", count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "RetryJob failed"); }
        }
    }
}
