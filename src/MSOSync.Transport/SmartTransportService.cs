using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Transport;

public sealed class SmartTransportService(
    INodeMetadataService           nodeMetadata,
    PushClient                     pushClient,
    IBatchStateMachine             stateMachine,
    AcknowledgementService         acknowledgement,
    ITransportFailureClassifier    classifier,
    IClock                         clock,
    ILogger<SmartTransportService> logger) : ITransportService
{
    public async Task SendBatchAsync(
        SyncOutgoingBatch            batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken            ct = default)
    {
        var node = await nodeMetadata.GetNodeAsync(batch.NodeId, ct);

        if (node == null)
        {
            logger.LogWarning("Transport: node {NodeId} not found — skipping batch {BatchId}",
                batch.NodeId, batch.BatchId);
            return;
        }

        if (!node.SyncEnabled)
        {
            logger.LogDebug("Transport: node {NodeId} sync disabled — skipping batch {BatchId}",
                batch.NodeId, batch.BatchId);
            return;
        }

        if (node.TransportMode == TransportMode.Pull)
        {
            logger.LogDebug("Transport: node {NodeId} is Pull — batch {BatchId} awaits pull",
                batch.NodeId, batch.BatchId);
            return;
        }

        await stateMachine.MoveToSendingAsync(batch.BatchId, ct);

        try
        {
            var result  = await pushClient.PushAsync(node.SyncUrl, batch, events, ct);
            var ackTime = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, result.Success, ackTime, null, result.ErrorMessage, ct);
        }
        catch (Exception ex)
        {
            var reason  = classifier.Classify(ex);
            var ackTime = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
            logger.LogError(ex, "Transport: push failed for batch {BatchId} — reason={Reason}",
                batch.BatchId, reason);
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, false, ackTime, reason, ex.Message, ct);
        }
    }
}
