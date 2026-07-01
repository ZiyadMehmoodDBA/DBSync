using MSOSync.Metadata.Dtos;
using MSOSync.Security;

namespace MSOSync.Metadata.Interfaces;

public interface INodeMetadataService
{
    Task<IReadOnlyList<NodeDto>> GetNodesAsync(CancellationToken ct = default);
    Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<NodeGroupDto>> GetNodeGroupsAsync(CancellationToken ct = default);
    Task<NodeDto> UpdateNodeAsync(string nodeId, UpdateNodeRequest req, CancellationToken ct = default);
    Task EnableNodeAsync(string nodeId, CancellationToken ct = default);
    Task DisableNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<RegistrationRequestDto>> GetPendingRegistrationsAsync(CancellationToken ct = default);
    Task<NodeProvisionResult> ApproveRegistrationAsync(long requestId, CancellationToken ct = default);
    Task RejectRegistrationAsync(long requestId, CancellationToken ct = default);
    Task<NodeSecurityInfoDto> GetNodeSecurityInfoAsync(string nodeId, CancellationToken ct = default);
    Task RecordHeartbeatAsync(string nodeId, DateTime heartbeatTime, CancellationToken ct = default);
    Task<CreateNodeResult> CreateNodeAsync(CreateNodeRequest req, CancellationToken ct = default);
}
