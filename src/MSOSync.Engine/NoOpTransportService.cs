using Microsoft.Extensions.Logging;
using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public sealed class NoOpTransportService(ILogger<NoOpTransportService> logger) : ITransportService
{
    public Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct = default)
    {
        logger.LogTrace("Transport not implemented. Batch {BatchId} skipped.", batch.BatchId);
        return Task.CompletedTask;
    }
}
