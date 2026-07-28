using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Metrics;

namespace MSOSync.Scheduler;

/// <summary>
/// Unit of work invoked per node per poll cycle by AdaptivePollingOrchestrator.
/// Demoted from BackgroundService in 2D.5 — no longer manages its own timer.
/// Uses ISchedulerLockFactory + SchedulerJobGuard for distributed locking.
/// </summary>
public sealed class SyncJob(
    IServiceScopeFactory     scopeFactory,
    ISchedulerLockFactory    lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry    registry,
    ILogger<SyncJob>         logger)
{
    // Parameterless overload retained for test backward compatibility
    internal Task RunTickAsync(CancellationToken ct = default) => RunTickAsync(null, ct);

    internal async Task RunTickAsync(string? nodeId, CancellationToken ct)
    {
        using var cycleActivity = PipelineActivitySource.Source.StartActivity("sync.cycle");
        cycleActivity?.SetTag("node.id", nodeId);

        registry.RecordTickStart(nameof(SyncJob));
        try
        {
            // Per-node lock key: "SyncJob:node-abc" or "SyncJob" (legacy/single-node)
            var lockKey = nodeId is not null ? $"{nameof(SyncJob)}:{nodeId}" : nameof(SyncJob);

            await SchedulerJobGuard.RunAsync(
                lockKey,
                lockFactory,
                health,
                logger,
                async innerCt =>
                {
                    using var dispatchActivity = PipelineActivitySource.Source.StartActivity("sync.dispatch");
                    dispatchActivity?.SetTag("node.id", nodeId);

                    await using var scope = scopeFactory.CreateAsyncScope();
                    var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();
                    await engine.RunAsync(innerCt);

                    dispatchActivity?.SetStatus(ActivityStatusCode.Ok);
                },
                ct);

            cycleActivity?.SetStatus(ActivityStatusCode.Ok);
            registry.RecordTickComplete(nameof(SyncJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            cycleActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            registry.RecordTickFailed(nameof(SyncJob), ex);
            logger.LogError(ex, "SyncJob run failed for node {NodeId}", nodeId);
        }
    }
}
