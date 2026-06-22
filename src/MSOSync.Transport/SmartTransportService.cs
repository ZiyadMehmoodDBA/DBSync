using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Engine;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Transport;

public sealed class SmartTransportService(
    INodeMetadataService            nodeMetadata,
    PushClient                      pushClient,
    IBatchStateMachine              stateMachine,
    AcknowledgementService          acknowledgement,
    ITransportFailureClassifier     classifier,
    ILogger<SmartTransportService>  logger) : ITransportService
{
    // Public interface matches current ITransportService signature (Task 8 adds the events param).
    public Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct = default)
        => SendBatchCoreAsync(batch, [], ct);

    private async Task SendBatchCoreAsync(
        SyncOutgoingBatch            batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken            ct)
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
            // PULL target: source keeps batch as New; PullJob on the target will come fetch it.
            logger.LogDebug("Transport: node {NodeId} is Pull — batch {BatchId} awaits pull",
                batch.NodeId, batch.BatchId);
            return;
        }

        // PUSH target: initiate send.
        await stateMachine.MoveToSendingAsync(batch.BatchId, ct);

        try
        {
            var result = await pushClient.PushAsync(node.SyncUrl, batch, events, ct);
            var ackTime = DateTimeOffset.UtcNow;
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, result.Success, ackTime, result.ErrorMessage, ct);
        }
        catch (Exception ex)
        {
            var reason = classifier.Classify(ex);
            logger.LogError(ex, "Transport: push failed for batch {BatchId} — reason={Reason}",
                batch.BatchId, reason);
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, success: false, DateTimeOffset.UtcNow, ex.Message, ct);
        }
    }
}
