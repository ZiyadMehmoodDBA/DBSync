using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Scheduler;

public sealed class SchedulerRecovery(
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerRecovery> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stateMachine = scope.ServiceProvider.GetRequiredService<IBatchStateMachine>();
        var clock        = scope.ServiceProvider.GetRequiredService<IClock>();
        var mediator     = scope.ServiceProvider.GetRequiredService<IMediator>();
        var now          = clock.UtcNow;

        // 1. Sending → Error (crash during PUSH — only batches stuck > 5 min)
        var staleThreshold = now.AddMinutes(-5);
        var sendingBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Sending
                     && b.SentTime != null
                     && b.SentTime < staleThreshold)
            .ToListAsync(ct);

        var sendingRecovered = 0;
        foreach (var b in sendingBatches)
        {
            if (await stateMachine.MoveToErrorAsync(b.BatchId, ct))
            {
                sendingRecovered++;
                logger.LogInformation("Recovery {Reason}: Batch {BatchId} Sending→Error (stale {SentTime})",
                    RecoveryReason.Restart, b.BatchId, b.SentTime);
            }
        }

        // 2. RETRY with overdue next_retry_time → requeue
        var overdueBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Retry
                     && b.NextRetryTime != null
                     && b.NextRetryTime <= now)
            .ToListAsync(ct);

        var retryRequeued = 0;
        foreach (var b in overdueBatches)
        {
            b.NextRetryTime = null;
            retryRequeued++;
            logger.LogInformation("Recovery {Reason}: Batch {BatchId} overdue retry requeued",
                RecoveryReason.OverdueRetry, b.BatchId);
        }

        if (retryRequeued > 0) await db.SaveChangesAsync(ct);

        // 3. NEW batches older than 10 min — never sent (restart scenario)
        var staleTime = now.AddMinutes(-10);
        var newBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.New && b.CreateTime < staleTime)
            .ToListAsync(ct);

        var newRecovered = 0;
        foreach (var b in newBatches)
        {
            if (await stateMachine.MoveToRetryAsync(b.BatchId, ct))
            {
                newRecovered++;
                logger.LogInformation("Recovery {Reason}: Batch {BatchId} New→Retry",
                    RecoveryReason.Restart, b.BatchId);
            }
        }

        logger.LogInformation(
            "SchedulerRecovery complete: sendingRecovered={S} retryRequeued={R} newRecovered={N}",
            sendingRecovered, retryRequeued, newRecovered);

        await mediator.Publish(
            new SchedulerRecoveryEvent(sendingRecovered, retryRequeued, newRecovered), ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
