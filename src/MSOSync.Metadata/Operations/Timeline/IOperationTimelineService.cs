using MSOSync.Metadata.Operations.Timeline.Dtos;

namespace MSOSync.Metadata.Operations.Timeline;

public interface IOperationTimelineService
{
    Task<OperationTimelineDto> GetTimelineAsync(
        DateTime   from,
        DateTime   to,
        string[]?  types,
        int        limit,
        CancellationToken ct = default);
}
