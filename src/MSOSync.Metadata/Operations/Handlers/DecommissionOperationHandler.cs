using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Metadata.Operations.Handlers;

/// <summary>
/// Handles cancel for Decommission operations by transitioning the node from
/// Decommissioning back to Disabled via INodeLifecycleService.CancelDecommissionAsync.
///
/// The referenceId in the operation row is null for decommission (node IDs are strings).
/// The actorId is a Guid passed by OperationService, derived from the JWT NameIdentifier claim.
/// The nodeId is carried in the operation's CorrelationId column.
/// </summary>
public sealed class DecommissionOperationHandler(
    INodeLifecycleService lifecycle) : IOperationHandler
{
    public OperationType OperationType => OperationType.Decommission;

    public Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
        // Not called by the generic OperationService path because referenceId is null for
        // decommission operations. The patched OperationService.CancelAsync calls
        // CancelByCorrelationAsync instead when referenceId is null and correlationId is set.
        => throw new InvalidOperationException(
            "DecommissionOperationHandler.CancelAsync should not be called directly. " +
            "OperationService routes decommission cancellations via CancelByCorrelationAsync.");

    public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
        => throw new NotSupportedException(
            "Decommission retry is not supported. Use ForceCompleteDecommission or restart the process.");

    /// <summary>
    /// Called by the patched OperationService when the operation has no ReferenceId
    /// but has a CorrelationId (the nodeId string for decommission).
    /// actorId is the Guid parsed from the JWT NameIdentifier claim; since SyncUser.UserId
    /// is long, we use actorId.ToString() as the actor username string.
    /// </summary>
    public Task CancelByCorrelationAsync(string nodeId, Guid actorId, CancellationToken ct)
    {
        // SyncUser.UserId is a long — we cannot query it by Guid.
        // Use actorId.ToString() as a best-effort actor identifier for the audit trail.
        var actor = actorId == Guid.Empty ? "operator" : actorId.ToString();
        return lifecycle.CancelDecommissionAsync(nodeId, actor, ct);
    }
}
