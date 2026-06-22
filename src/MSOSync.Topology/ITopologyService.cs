namespace MSOSync.Topology;

public interface ITopologyService
{
    /// <summary>
    /// Returns all Active sync nodes that are not the local node.
    /// CE assumption: flat topology, every non-self Active node is a source.
    /// </summary>
    Task<IReadOnlyList<SourceNodeInfo>> GetSourceNodesAsync(
        string localNodeId, CancellationToken ct = default);
}
