namespace MSOSync.Metadata.OutgoingBatches;

public interface IOutgoingBatchQueryService
{
    Task<OutgoingBatchPage> GetBatchesAsync(OutgoingBatchQueryFilter filter, CancellationToken ct = default);
    Task<OutgoingBatchRow?> GetBatchByIdAsync(long batchId, CancellationToken ct = default);
}
