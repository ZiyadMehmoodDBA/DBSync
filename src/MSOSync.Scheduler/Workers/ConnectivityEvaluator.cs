using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;

namespace MSOSync.Scheduler.Workers;

/// SOLE writer of ConnectivityStatus + ConnectivityReason (Invariant 3, spec §5.1).
/// Skips a cycle if the previous evaluation is still running (spec §5.1).
public sealed class ConnectivityEvaluator(
    IServiceScopeFactory scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IOptions<LifecycleOptions> lifecycleOptions,
    IOptions<HeartbeatOptions> heartbeatOptions,
    IWorkerStatusRegistry registry,
    ILogger<ConnectivityEvaluator> logger) : BackgroundService
{
    private int _running;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(nameof(ConnectivityEvaluator),
            TimeSpan.FromSeconds(lifecycleOptions.Value.ConnectivityEvaluatorIntervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var topology = scope.ServiceProvider.GetRequiredService<ITopologyService>();
            if (!await topology.IsHubAsync(props.NodeId, ct))
            {
                logger.LogInformation("ConnectivityEvaluator disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.ConnectivityEvaluatorIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                logger.LogWarning("ConnectivityEvaluator cycle skipped — previous evaluation still running");
                continue;
            }
            registry.RecordTickStart(nameof(ConnectivityEvaluator));
            try
            {
                await RunCycleAsync(ct);
                registry.RecordTickComplete(nameof(ConnectivityEvaluator));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(ConnectivityEvaluator), ex);
                logger.LogError(ex, "ConnectivityEvaluator cycle failed");
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db       = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var policy   = scope.ServiceProvider.GetRequiredService<IConnectivityPolicy>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var heartbeatInterval = TimeSpan.FromSeconds(heartbeatOptions.Value.IntervalSeconds);
        var probeInterval     = TimeSpan.FromSeconds(heartbeatOptions.Value.ProbeIntervalSeconds);
        var now = DateTime.UtcNow;

        // Exclude terminal states — Decommissioned and Rejected nodes never send heartbeats
        // or receive probes; skipping them avoids unnecessary DB work (Task 7 minor fix).
        var nodes = await db.Nodes
            .Where(n => n.LifecycleState != NodeLifecycleState.Decommissioned
                     && n.LifecycleState != NodeLifecycleState.Rejected)
            .ToListAsync(ct);
        var changes = new List<NodeConnectivityChangedEvent>();

        foreach (var node in nodes)
        {
            var result = policy.Evaluate(new ConnectivityTelemetry(
                node.LifecycleState,
                node.LastHeartbeat,
                node.LastProbeTime,
                LastProbeFailed: node.LastProbeError is not null,
                node.ConsecutiveProbeFailures,
                now, heartbeatInterval, probeInterval));

            if (node.ConnectivityStatus == result.Status && node.ConnectivityReason == result.Reason)
                continue;

            var previous = node.ConnectivityStatus;
            node.ConnectivityStatus = result.Status;
            node.ConnectivityReason = result.Reason;

            if (previous != result.Status)
            {
                db.NodeConnectivityHistories.Add(new SyncNodeConnectivityHistory
                {
                    NodeId = node.NodeId,
                    PreviousStatus = previous,
                    NewStatus = result.Status,
                    Reason = result.Reason,
                    OccurredAt = DateTimeOffset.UtcNow,
                });
                changes.Add(new NodeConnectivityChangedEvent(node.NodeId, previous, result.Status));
            }
        }

        // Prune connectivity history past retention (spec §3.3) — same cycle, cheap delete
        var cutoff = DateTimeOffset.UtcNow.AddDays(-lifecycleOptions.Value.ConnectivityHistoryRetentionDays);
        await db.NodeConnectivityHistories.Where(h => h.OccurredAt < cutoff).ExecuteDeleteAsync(ct);

        // RowVersion is a concurrency token — a race with a lifecycle command can throw here.
        // Connectivity writes are idempotent; the next cycle re-evaluates.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogDebug("ConnectivityEvaluator lost a concurrency race; next cycle re-evaluates");
            return;   // do not publish events for uncommitted changes
        }

        // Publish AFTER commit (same discipline as lifecycle events)
        foreach (var evt in changes)
            await mediator.Publish(evt, ct);
    }
}
