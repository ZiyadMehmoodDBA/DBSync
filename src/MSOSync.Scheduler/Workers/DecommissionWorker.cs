using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;

namespace MSOSync.Scheduler.Workers;

/// Finalizes drains ONLY through NodeLifecycleService.FinalizeDecommissionAsync — no side door
/// (spec §4.7, §5.5). Never writes lifecycle state directly.
public sealed class DecommissionWorker(
    IServiceScopeFactory        scopeFactory,
    IOptions<NodeProperties>    nodeProps,
    IOptions<LifecycleOptions>  lifecycleOptions,
    ILogger<DecommissionWorker> logger,
    IWorkerStatusRegistry       registry) : BackgroundService
{
    private int _running;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.DecommissionWorkerIntervalSeconds > 0
            ? lifecycleOptions.Value.DecommissionWorkerIntervalSeconds
            : 30);
        registry.Register(nameof(DecommissionWorker), interval);
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
                logger.LogInformation("DecommissionWorker disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.DecommissionWorkerIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                logger.LogWarning("DecommissionWorker tick skipped — previous tick still running");
                continue;
            }
            registry.RecordTickStart(nameof(DecommissionWorker));
            try
            {
                await RunTickAsync(ct);
                registry.RecordTickComplete(nameof(DecommissionWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(DecommissionWorker), ex);
                logger.LogError(ex, "DecommissionWorker tick failed");
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IDecommissionEvaluator>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<INodeLifecycleService>();

        var draining = await db.Nodes.AsNoTracking()
            .Where(n => n.LifecycleState == NodeLifecycleState.Decommissioning)
            .ToListAsync(ct);

        foreach (var node in draining)
        {
            var decision = await evaluator.EvaluateAsync(node, ct);
            if (!decision.Finalize)
            {
                logger.LogDebug("Node {NodeId} still draining ({Reason})", node.NodeId, decision.Reason);
                continue;
            }

            var trigger = decision.Reason == DecommissionDecisionReason.GraceExpired
                ? LifecycleTrigger.Timeout
                : LifecycleTrigger.System;
            try
            {
                await lifecycle.FinalizeDecommissionAsync(
                    node.NodeId, trigger, decision.Reason.ToString(), ct);
                logger.LogInformation("Node {NodeId} decommission finalized ({Reason})", node.NodeId, decision.Reason);
            }
            catch (Exception ex) when (ex is ConcurrencyException or InvalidLifecycleTransitionException)
            {
                // Operator force-completed (or other command won the race) — next tick reconciles.
                logger.LogDebug(ex, "Node {NodeId} finalize lost a race; skipping", node.NodeId);
            }
        }
    }
}
