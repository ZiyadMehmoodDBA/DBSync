using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface IRouterMetadataService
{
    Task<IReadOnlyList<RouterDto>> GetRoutersAsync(CancellationToken ct = default);
    Task<RouterDto?> GetRouterAsync(string routerId, CancellationToken ct = default);
    Task<IReadOnlyList<RouterDto>> GetRoutersForSourceGroupAsync(string groupId, CancellationToken ct = default);
    Task<IReadOnlyList<RouterDto>> GetRoutersForTargetGroupAsync(string groupId, CancellationToken ct = default);
    Task<RouterDto> CreateRouterAsync(CreateRouterRequest req, CancellationToken ct = default);
    Task<RouterDto> UpdateRouterAsync(string routerId, UpdateRouterRequest req, CancellationToken ct = default);
    Task DeleteRouterAsync(string routerId, CancellationToken ct = default);
}
