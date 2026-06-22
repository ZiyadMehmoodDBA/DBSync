using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

public interface IApplyService
{
    Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default);
}
