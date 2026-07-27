using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common.Workers;

namespace MSOSync.Scheduler;

public sealed class RetryJob(
    IServiceScopeFactory     scopeFactory,
    ISchedulerLockFactory    lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry    registry,
    ILogger<RetryJob>        logger) : BackgroundService
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
            await SchedulerJobGuard.RunAsync(
                nameof(RetryJob),
                lockFactory,
                health,
                logger,
                async innerCt =>
                {
                    await using var scope    = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<RetryProcessor>();
                    var count     = await processor.ProcessAsync(innerCt);
                    if (count > 0)
                        logger.LogInformation("RetryJob queued {Count} batches for retry", count);
                },
                ct);

            registry.RecordTickComplete(nameof(RetryJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(RetryJob), ex);
            logger.LogError(ex, "RetryJob failed");
        }
    }
}
