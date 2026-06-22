using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public interface ITransportService
{
    Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct = default);
}
