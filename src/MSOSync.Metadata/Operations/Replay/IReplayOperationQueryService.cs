using MSOSync.Common.Pagination;
using MSOSync.Metadata.Operations.Replay.Dtos;

namespace MSOSync.Metadata.Operations.Replay;

public interface IReplayOperationQueryService
{
    Task<ReplayOperationDetailDto?> GetDetailAsync(Guid operationId, CancellationToken ct = default);
    Task<CursorPageResult<ReplayItemDto>> GetItemsAsync(
        Guid operationId, ReplayItemFilter filter, CancellationToken ct = default);
}
