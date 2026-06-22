using MediatR;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Event;
using MSOSync.Routing;
using MSOSync.Trigger;

namespace MSOSync.Engine;

public sealed class SyncEngine(
    ITriggerDriftDetector driftDetector,
    IEventReader eventReader,
    IRoutingService routingService,
    IBatchCreator batchCreator,
    ITransportService transport,
    IMediator mediator,
    IClock clock,
    ILogger<SyncEngine> logger)
{
    private const int BatchReadSize = 1000;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var start = clock.UtcNow;
        logger.LogDebug("SyncEngine.RunAsync starting");

        // 1. Drift detection — log only, never block
        try { await driftDetector.DetectAllAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Drift detection failed — continuing"); }

        // 2. Read unprocessed events
        var events = await eventReader.ReadAsync(BatchReadSize, ct);
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

        // 5. Send each batch via transport (PUSH or PULL no-op)
        foreach (var batch in batches)
            await transport.SendBatchAsync(batch, events, ct);

        // 6. Publish cycle event
        var duration = clock.UtcNow - start;
        logger.LogInformation("SyncEngine: read={Events} batches={Batches} elapsed={Elapsed}",
            events.Count, batches.Count, duration);
        await mediator.Publish(new SyncCycleCompletedEvent(events.Count, batches.Count, duration), ct);
    }
}
