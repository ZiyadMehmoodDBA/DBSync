using MSOSync.Metadata.Operations.Replay.Dtos;

namespace MSOSync.Metadata.Operations.Replay;

public interface IReplayOperationService
{
    Task<ReplayOperationCreatedDto> CreateAsync(CreateReplayRequest req, CancellationToken ct = default);
    Task CancelAsync(Guid operationId, CancellationToken ct = default);
}
