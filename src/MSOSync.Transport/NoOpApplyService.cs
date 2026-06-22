using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

/// <summary>
/// Apply stub. Transitions batch New→Applying→Applied and returns success.
/// Epic 7 replaces this with the real SQL apply engine.
/// </summary>
public sealed class NoOpApplyService(
    AppDbContext              db,
    IClock                    clock,
    ILogger<NoOpApplyService> logger) : IApplyService
{
    public async Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default)
    {
        incoming.Status = IncomingBatchStatus.Applying;
        await db.SaveChangesAsync(ct);

        logger.LogDebug("NoOpApplyService: applying batch {BatchId} ({RowCount} rows) — stub",
            incoming.BatchId, payload.RowCount);

        incoming.Status      = IncomingBatchStatus.Applied;
        incoming.AppliedTime = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return new ApplyResult(true, payload.RowCount, 0, null);
    }
}
