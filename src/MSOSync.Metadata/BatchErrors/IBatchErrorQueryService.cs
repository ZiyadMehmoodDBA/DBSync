using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.BatchErrors;

public interface IBatchErrorQueryService
{
    Task<PagedResult<BatchErrorSummaryDto>> GetBatchErrorsAsync(
        BatchErrorFilter filter, CancellationToken ct = default);

    Task<BatchErrorDetailDto?> GetBatchErrorByIdAsync(
        long errorId, CancellationToken ct = default);

    Task<BatchErrorSummaryCountDto> GetBatchErrorSummaryAsync(
        long? batchId, DateTime? from, DateTime? to, CancellationToken ct = default);
}
