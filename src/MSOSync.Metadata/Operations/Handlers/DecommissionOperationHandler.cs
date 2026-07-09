using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Metadata.Operations.Handlers;

/// <summary>
/// Delegates Decommission operation cancel to INodeLifecycleService.
/// The referenceId is the SyncNode.NodeId encoded as a GUID (see note below).
/// </summary>
public sealed class DecommissionOperationHandler : IOperationHandler
{
    // Injected for Task 5 wiring; not yet used in stubs.
    private readonly INodeLifecycleService _lifecycle;

    public DecommissionOperationHandler(INodeLifecycleService lifecycle)
        => _lifecycle = lifecycle;

    public OperationType OperationType => OperationType.Decommission;

    public Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
    {
        // NodeId is varchar(50), not a GUID. The referenceId here is the operation's
        // reference_id column, which for decommission was set to the node's internal
        // metadata_json when CreateAsync was called by NodeLifecycleService.
        //
        // For now, CancelDecommissionAsync is a stub — it must be added to INodeLifecycleService
        // in Task 5. This placeholder throws until that method is wired.
        //
        // When Task 5 is complete, replace this with:
        //   await _lifecycle.CancelDecommissionAsync(referenceId.ToString(), actorId.ToString(), ct);
        throw new NotSupportedException(
            "Decommission cancellation via INodeLifecycleService.CancelDecommissionAsync " +
            "is wired in Task 5 (epic12c-task-5-domain-integration).");
    }

    public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Decommission retry is not supported. Use ForceCompleteDecommission or restart the process.");
    }
}
