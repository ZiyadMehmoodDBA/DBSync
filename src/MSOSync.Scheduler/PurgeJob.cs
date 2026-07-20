using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Event;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class PurgeJob(
    IServiceScopeFactory  scopeFactory,
    IClock                clock,
    IWorkerStatusRegistry registry,
    ILogger<PurgeJob>     logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Task.Delay used intentionally — PurgeJob targets wall-clock 02:00 UTC, not a fixed interval
        registry.Register(nameof(PurgeJob), TimeSpan.FromHours(24));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = TimeUntilNextFire();
            logger.LogDebug("PurgeJob sleeping {Delay} until next 02:00 UTC", delay);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }

            registry.RecordTickStart(nameof(PurgeJob));
            try
            {
                await RunPurgeAsync(ct);
                registry.RecordTickComplete(nameof(PurgeJob));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(PurgeJob), ex);
                logger.LogError(ex, "PurgeJob failed");
            }
        }
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        await using var scope        = scopeFactory.CreateAsyncScope();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
        var eventPurger  = scope.ServiceProvider.GetRequiredService<IEventPurger>();
        var batchPurger  = scope.ServiceProvider.GetRequiredService<BatchPurger>();

        await using var lease = await lockProvider.TryAcquireAsync(LockNames.PurgeEngine, ct);
        if (lease == null) { logger.LogDebug("PurgeJob: lock held, skipping"); return; }

        var events  = await eventPurger.PurgeAsync(ct);
        var batches = await batchPurger.PurgeAsync(ct);
        logger.LogInformation("PurgeJob: deleted {Events} events, {Batches} batches", events, batches);
    }

    private TimeSpan TimeUntilNextFire()
    {
        var now  = clock.UtcNow;
        var next = now.Date.AddHours(2);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
