using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Engine;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<SyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue<int>("Sync:IntervalSeconds", 30));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
            var engine       = scope.ServiceProvider.GetRequiredService<SyncEngine>();

            await using var lease = await lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
            if (lease == null)
            {
                logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
                continue;
            }

            try { await engine.RunAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "SyncJob run failed"); }
        }
    }
}
