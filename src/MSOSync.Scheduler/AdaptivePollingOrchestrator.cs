using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Interfaces;
using MSOSync.Scheduler.Options;

namespace MSOSync.Scheduler;

/// <summary>
/// Drives adaptive per-node poll loops. Replaces the fixed PeriodicTimer in SyncJob.
/// One Task is spawned per active node. A refresh loop detects newly added nodes every
/// NodeRefreshIntervalSeconds (default 60 s). Registered as a singleton BackgroundService.
/// </summary>
public sealed class AdaptivePollingOrchestrator(
    IServiceScopeFactory             scopeFactory,
    IAdaptivePollingService          pollingService,
    IOptions<AdaptivePollingOptions> pollingOptions,
    IWorkerStatusRegistry            registry,
    ILogger<AdaptivePollingOrchestrator> logger) : BackgroundService
{
    private const int NodeRefreshIntervalSeconds = 60;

    // Tracks one Task per nodeId. The value is a running Task; never awaited here —
    // the CancellationToken passed to each loop drives termination.
    private readonly ConcurrentDictionary<string, Task> _nodeTasks = new();

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(
            nameof(AdaptivePollingOrchestrator),
            TimeSpan.FromSeconds(pollingOptions.Value.BaseIntervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial node load
        await RefreshNodesAsync(ct);

        using var refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(NodeRefreshIntervalSeconds));
        while (await refreshTimer.WaitForNextTickAsync(ct))
        {
            try { await RefreshNodesAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "AdaptivePollingOrchestrator: node refresh failed"); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken); // signals ct passed to ExecuteAsync

        // Drain active node tasks with a 10-second timeout
        var allTasks = _nodeTasks.Values.ToArray();
        if (allTasks.Length == 0) return;

        var drain   = Task.WhenAll(allTasks);
        var timeout = Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.WhenAny(drain, timeout);

        if (!drain.IsCompleted)
            logger.LogWarning("AdaptivePollingOrchestrator: some node tasks did not finish within drain timeout");
    }

    // -----------------------------------------------------------------------

    private async Task RefreshNodesAsync(CancellationToken ct)
    {
        var activeNodeIds = await LoadActiveNodeIdsAsync(ct);

        // Spawn tasks for newly discovered nodes
        foreach (var nodeId in activeNodeIds)
        {
            if (!_nodeTasks.ContainsKey(nodeId))
            {
                logger.LogDebug("AdaptivePollingOrchestrator: starting dispatch loop for node {NodeId}", nodeId);
                var nodeTask = RunNodeLoopAsync(nodeId, ct);
                _nodeTasks.TryAdd(nodeId, nodeTask);
            }
        }

        // Prune completed tasks (node decommissioned / loop exited)
        foreach (var (nodeId, task) in _nodeTasks)
        {
            if (task.IsCompleted)
            {
                _nodeTasks.TryRemove(nodeId, out _);
                logger.LogDebug("AdaptivePollingOrchestrator: pruned completed task for node {NodeId}", nodeId);
            }
        }
    }

    private async Task<IReadOnlyList<string>> LoadActiveNodeIdsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope   = scopeFactory.CreateAsyncScope();
            var nodeMeta = scope.ServiceProvider.GetRequiredService<INodeMetadataService>();
            // Use the id-only query to avoid materialising full DTO objects for every node
            // on every 60-second refresh tick (C4 fix).
            return await nodeMeta.GetActiveNodeIdsAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AdaptivePollingOrchestrator: failed to load active nodes");
            return Array.Empty<string>();
        }
    }

    private async Task RunNodeLoopAsync(string nodeId, CancellationToken ct)
    {
        logger.LogInformation("AdaptivePollingOrchestrator: starting poll loop for node {NodeId}", nodeId);

        while (!ct.IsCancellationRequested)
        {
            registry.RecordTickStart(nameof(AdaptivePollingOrchestrator));
            bool hadWork = false;
            try
            {
                await using var scope  = scopeFactory.CreateAsyncScope();
                var syncJob = scope.ServiceProvider.GetRequiredService<SyncJob>();

                // RunTickAsync returns implicitly; we detect work by checking whether engine ran
                // (engine publishes SyncCycleCompletedEvent — but here we track exception vs success)
                await syncJob.RunTickAsync(nodeId, ct);

                // If no exception, treat as success. Actual hadWork signal comes from SyncEngine
                // publishing SyncCycleCompletedEvent, but we approximate it here:
                // A future iteration can subscribe to SyncCycleCompletedEvent to get the exact count.
                // For now: assume hadWork=true (conservative — keeps interval from backing off needlessly).
                hadWork = true;

                registry.RecordTickComplete(nameof(AdaptivePollingOrchestrator));
                await pollingService.RecordActivityAsync(nodeId, hadWork, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AdaptivePollingOrchestrator: tick failed for node {NodeId}", nodeId);
                registry.RecordTickFailed(nameof(AdaptivePollingOrchestrator), ex);
                await pollingService.RecordErrorAsync(nodeId, ct);
            }

            // Sleep for the adaptive interval before next tick
            var interval = await pollingService.GetIntervalAsync(nodeId, ct);
            logger.LogDebug("AdaptivePollingOrchestrator: node {NodeId} sleeping for {Interval}s",
                nodeId, interval.TotalSeconds);
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("AdaptivePollingOrchestrator: poll loop exiting for node {NodeId}", nodeId);
    }
}
