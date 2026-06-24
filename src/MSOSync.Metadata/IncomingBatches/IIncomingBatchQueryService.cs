using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.IncomingBatches;

public interface IIncomingBatchQueryService
{
    Task<PagedResult<IncomingBatchSummaryDto>> GetIncomingBatchesAsync(
        IncomingBatchFilter filter, CancellationToken ct = default);

    Task<IncomingBatchDetailDto?> GetIncomingBatchByIdAsync(
        long batchId, CancellationToken ct = default);
}
