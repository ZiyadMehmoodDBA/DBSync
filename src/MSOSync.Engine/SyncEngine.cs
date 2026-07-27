using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Event;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Trigger;

namespace MSOSync.Engine;

public sealed class SyncEngine(
    ITriggerDriftDetector   driftDetector,
    IEventReader            eventReader,
    IRoutingService         routingService,
    IBatchCreator           batchCreator,
    IServiceScopeFactory    scopeFactory,
    IMediator               mediator,
    IMetricsService         metrics,
    IClock                  clock,
    ILogger<SyncEngine>     logger)
{
    private const int BatchReadSize = 1000;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var start = clock.UtcNow;
        logger.LogDebug("SyncEngine.RunAsync starting");

        // 1. Drift detection — log only, never block
        try { await driftDetector.DetectAllAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Drift detection failed — continuing"); }

        // 2. Read unprocessed events (instrumented)
        var fetchSw = Stopwatch.StartNew();
        var events  = await eventReader.ReadAsync(BatchReadSize, ct);
        fetchSw.Stop();
        metrics.RecordHistogram("sync.pipeline.fetch_ms", fetchSw.Elapsed.TotalMilliseconds);

        if (events.Count == 0)
        {
            logger.LogDebug("SyncEngine: no events to process");
            await mediator.Publish(new SyncCycleCompletedEvent(0, 0, clock.UtcNow - start), ct);
            return;
        }

        // 3. Resolve routes for each event
        var routes = new Dictionary<long, IReadOnlyList<string>>();
        foreach (var evt in events)
            routes[evt.EventId] = await routingService.ResolveAsync(evt.TriggerId, ct);

        // 4. Create batches
        var batches = await batchCreator.CreateBatchesAsync(events, routes, ct);

        // 5. Parallel dispatch: group by NodeId, one IServiceScope per group
        //    Batches within a node group are dispatched serially (preserves sequence order per channel).
        //    Batches for different nodes are dispatched concurrently.
        var byNode = batches.GroupBy(b => b.NodeId).ToList();

        await Task.WhenAll(byNode.Select(group =>
            DispatchNodeBatchesAsync(group.Key, group.ToList(), events, ct)));

        // 6. Publish cycle event
        var duration = clock.UtcNow - start;
        logger.LogInformation("SyncEngine: read={Events} batches={Batches} elapsed={Elapsed}",
            events.Count, batches.Count, duration);
        await mediator.Publish(new SyncCycleCompletedEvent(events.Count, batches.Count, duration), ct);
    }

    /// <summary>
    /// Dispatches all batches for a single target node using a dedicated IServiceScope.
    /// Batches within the scope are sent serially to preserve per-channel sequence order.
    /// send_ms is instrumented per-batch inside SmartTransportService.SendBatchAsync.
    /// </summary>
    private async Task DispatchNodeBatchesAsync(
        string                           nodeId,
        IReadOnlyList<SyncOutgoingBatch> nodeBatches,
        IReadOnlyList<SyncDataEvent>     events,
        CancellationToken                ct)
    {
        await using var scope     = scopeFactory.CreateAsyncScope();
        var scopedTransport = scope.ServiceProvider.GetRequiredService<ITransportService>();

        foreach (var batch in nodeBatches)
            await scopedTransport.SendBatchAsync(batch, events, ct);
    }
}
