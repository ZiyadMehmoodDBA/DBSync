using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public interface IBatchTransportQueryService
{
    /// <summary>
    /// Returns the next New/Retry outgoing batch for the given target node and channel
    /// with sequence > afterSequence, plus whether more batches exist.
    /// Uses Take(2) probe: MoreAvailable = true if there is a batch after this one.
    /// </summary>
    Task<(SyncOutgoingBatch? Batch, bool MoreAvailable)> GetNextPullBatchAsync(
        string targetNodeId, string channelId, long afterSequence,
        CancellationToken ct = default);

    Task<IReadOnlyList<SyncDataEvent>> GetEventsForBatchAsync(
        long batchId, CancellationToken ct = default);

    /// <summary>
    /// Returns the maximum batch_sequence for (sourceNodeId, channelId) in sync_incoming_batch,
    /// or 0 if no batches exist yet.
    /// </summary>
    Task<long> GetLastSequenceAsync(
        string sourceNodeId, string channelId,
        CancellationToken ct = default);

    Task<bool> IncomingBatchExistsAsync(
        string sourceNodeId, long batchSequence,
        CancellationToken ct = default);

    Task InsertIncomingBatchAsync(
        SyncIncomingBatch batch, CancellationToken ct = default);
}
