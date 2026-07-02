using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.IncomingBatches;

public interface IIncomingBatchQueryService
{
    Task<CursorPageResult<IncomingBatchSummaryDto>> GetIncomingBatchesAsync(
        IncomingBatchFilter filter, CancellationToken ct = default);

    Task<IncomingBatchDetailDto?> GetIncomingBatchByIdAsync(
        long batchId, CancellationToken ct = default);
}
