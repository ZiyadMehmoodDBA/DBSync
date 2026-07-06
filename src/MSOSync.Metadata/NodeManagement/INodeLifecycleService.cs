namespace MSOSync.Metadata.NodeManagement;

public interface INodeLifecycleService
{
    Task<long> RegisterAsync(InboundRegistrationDto dto, CancellationToken ct = default);
    Task ApproveAsync(long id, string? notes, string actorUsername, CancellationToken ct = default);
    Task RejectAsync(long id, string? reason, string actorUsername, CancellationToken ct = default);
    Task<IReadOnlyList<BulkResultItemDto>> BulkApproveAsync(
        IReadOnlyList<long> ids, string actorUsername, CancellationToken ct = default);
    Task<IReadOnlyList<BulkResultItemDto>> BulkRejectAsync(
        IReadOnlyList<long> ids, string? reason, string actorUsername, CancellationToken ct = default);
    Task<ProvisionResultDto> ProvisionAsync(
        ProvisionRequestDto dto, string actorUsername, CancellationToken ct = default);
}
