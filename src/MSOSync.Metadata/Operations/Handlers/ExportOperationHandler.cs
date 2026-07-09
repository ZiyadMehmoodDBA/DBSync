using MSOSync.Metadata.Export;

namespace MSOSync.Metadata.Operations.Handlers;

/// <summary>
/// Delegates Export operation cancel/retry to IExportJobService.
/// The referenceId is the SyncExportJob.JobId.
/// </summary>
public sealed class ExportOperationHandler(IExportJobService exportJobService) : IOperationHandler
{
    public OperationType OperationType => OperationType.Export;

    public async Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
    {
        // SoftDeleteJobAsync sets status = Deleted, which halts the worker loop.
        // If the job is already terminal, this is a no-op inside SoftDeleteJobAsync.
        await exportJobService.SoftDeleteJobAsync(referenceId, ct);
    }

    public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
    {
        // Export retry: reset the export job row back to Pending so the worker picks it up.
        // IExportJobService does not expose a ResetToPendingAsync today; this is a stub
        // that throws until the export service implements it (tracked as 12C tech-debt item).
        throw new NotSupportedException(
            "Export job retry is not yet implemented. " +
            "Create a new export job instead of retrying.");
    }
}
