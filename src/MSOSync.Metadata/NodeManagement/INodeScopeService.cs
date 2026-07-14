// src/MSOSync.Metadata/NodeManagement/INodeScopeService.cs
namespace MSOSync.Metadata.NodeManagement;

public interface INodeScopeService
{
    Task<NodeScopeDto?> GetScopeAsync(string nodeId, CancellationToken ct = default);
    Task<NodeScopeDto>  SetScopeAsync(string nodeId, SetNodeScopeRequest req, string actor, CancellationToken ct = default);
}
