using MSOSync.Metadata.Common;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface INodeMetadataService
{
    Task<IReadOnlyList<NodeDto>> GetNodesAsync(CancellationToken ct = default);
    Task<PagedResult<NodeDto>> GetNodesPagedAsync(
        int pageNumber, int pageSize, CancellationToken ct = default);
    Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<NodeGroupDto>> GetNodeGroupsAsync(CancellationToken ct = default);
    Task<NodeDto> UpdateNodeAsync(string nodeId, UpdateNodeRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<RegistrationRequestDto>> GetPendingRegistrationsAsync(CancellationToken ct = default);
    Task RejectRegistrationAsync(long requestId, CancellationToken ct = default);
    Task<NodeSecurityInfoDto> GetNodeSecurityInfoAsync(string nodeId, CancellationToken ct = default);
    Task RecordHeartbeatAsync(string nodeId, DateTime heartbeatTime, CancellationToken ct = default);
    Task<CreateNodeResult> CreateNodeAsync(CreateNodeRequest req, CancellationToken ct = default);
}
