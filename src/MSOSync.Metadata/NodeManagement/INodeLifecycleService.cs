using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public interface INodeLifecycleService
{
    // ── 12A registration pipeline ──────────────────────────────────────────────
    Task<long> RegisterAsync(InboundRegistrationDto dto, CancellationToken ct = default);
    Task<ApproveResultDto> ApproveAsync(long id, string? notes, string actorUsername, CancellationToken ct = default);
    Task RejectAsync(long id, string? reason, string actorUsername, CancellationToken ct = default);
    Task<IReadOnlyList<BulkResultItemDto>> BulkApproveAsync(
        IReadOnlyList<long> ids, string actorUsername, CancellationToken ct = default);
    Task<IReadOnlyList<BulkResultItemDto>> BulkRejectAsync(
        IReadOnlyList<long> ids, string? reason, string actorUsername, CancellationToken ct = default);
    Task<ProvisionResultDto> ProvisionAsync(
        ProvisionRequestDto dto, string actorUsername, CancellationToken ct = default);

    // ── 12B-1 lifecycle command pipeline ───────────────────────────────────────

    // node-facing
    Task<ActivateResultDto> ActivateAsync(string externalId, string bootstrapToken, string agentVersion, CancellationToken ct = default);

    // operator commands
    Task EnableAsync(string nodeId, string actorUsername, CancellationToken ct = default);
    Task DisableAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default);
    Task StartMaintenanceAsync(string nodeId, string reason, DateTimeOffset? expectedEndAt, bool notifyNode, string actorUsername, CancellationToken ct = default);
    Task EndMaintenanceAsync(string nodeId, string actorUsername, CancellationToken ct = default);
    Task DecommissionAsync(string nodeId, string reason, int? gracePeriodMinutes, string actorUsername, CancellationToken ct = default);
    Task ForceCompleteDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default);

    // worker-only (System/Timeout trigger)
    Task FinalizeDecommissionAsync(string nodeId, LifecycleTrigger trigger, string reason, CancellationToken ct = default);

    // recovery approve/reject ride the existing registration ApproveAsync/RejectAsync by RegistrationType
}
