using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public interface IApplyService
{
    Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default);
}
