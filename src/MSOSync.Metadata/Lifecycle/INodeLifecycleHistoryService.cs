using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.Lifecycle;

public interface INodeLifecycleHistoryService
{
    /// <summary>
    /// Called ONLY by NodeLifecycleService (Invariant 2/10). Appends, never updates.
    /// Does NOT SaveChanges — participates in the command transaction.
    /// </summary>
    Task WriteTransitionAsync(LifecycleTransitionRecord record, CancellationToken ct = default);

    Task<PagedResult<LifecycleHistoryDto>> GetTimelineAsync(string nodeId, LifecycleHistoryFilter filter, CancellationToken ct = default);
    Task<LifecycleHistoryDto?> GetLatestAsync(string nodeId, CancellationToken ct = default);
    Task<NodeStateDto> GetCurrentStateAsync(string nodeId, CancellationToken ct = default);
}
