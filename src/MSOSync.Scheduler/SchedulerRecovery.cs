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

        // 1. SENT → RETRY (restart scenario — never ACKed)
        var sentBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Sent)
            .ToListAsync(ct);

        var sentRecovered = 0;
        foreach (var b in sentBatches)
        {
            if (await stateMachine.TransitionAsync(b.BatchId, BatchStatus.Sent, BatchStatus.Retry, ct))
            {
                sentRecovered++;
                logger.LogInformation("Recovery {Reason}: Batch {BatchId} SENT→RETRY",
                    RecoveryReason.Restart, b.BatchId);
            }
        }

        // 2. RETRY with overdue next_retry_time → requeue (reset to Retry so RetryJob picks up)
        var overdueBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Retry
                     && b.NextRetryTime != null
                     && b.NextRetryTime <= now)
            .ToListAsync(ct);

        var retryRequeued = 0;
        foreach (var b in overdueBatches)
        {
            // Already Retry status — just clear NextRetryTime so RetryJob re-schedules it
            b.NextRetryTime = null;
            retryRequeued++;
            logger.LogInformation("Recovery {Reason}: Batch {BatchId} overdue retry requeued",
                RecoveryReason.OverdueRetry, b.BatchId);
        }

        if (retryRequeued > 0) await db.SaveChangesAsync(ct);

        logger.LogInformation("SchedulerRecovery complete: sentRecovered={S} retryRequeued={R}",
            sentRecovered, retryRequeued);

        await mediator.Publish(new SchedulerRecoveryEvent(sentRecovered, retryRequeued), ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
