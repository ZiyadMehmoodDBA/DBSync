using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface ITriggerMetadataService
{
    Task<IReadOnlyList<TriggerDto>> GetTriggersAsync(CancellationToken ct = default);
    Task<TriggerDto?> GetTriggerAsync(string triggerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerDto>> GetTriggersForChannelAsync(string channelId, CancellationToken ct = default);
    Task<TriggerDto> CreateTriggerAsync(CreateTriggerRequest req, CancellationToken ct = default);
    Task<TriggerDto> UpdateTriggerAsync(string triggerId, UpdateTriggerRequest req, CancellationToken ct = default);
    Task DeleteTriggerAsync(string triggerId, CancellationToken ct = default);
    Task EnableTriggerAsync(string triggerId, CancellationToken ct = default);
    Task DisableTriggerAsync(string triggerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerRouterDto>> GetTriggerRoutersAsync(string triggerId, CancellationToken ct = default);
    Task AddTriggerRouterAsync(string triggerId, string routerId, CancellationToken ct = default);
    Task RemoveTriggerRouterAsync(string triggerId, string routerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerHistDto>> GetTriggerHistoryAsync(string triggerId, CancellationToken ct = default);
}
