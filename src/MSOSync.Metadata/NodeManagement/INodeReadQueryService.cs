using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public interface INodeReadQueryService
{
    Task<SyncNode?> GetNodeAsync(string nodeId, CancellationToken ct = default);
}
