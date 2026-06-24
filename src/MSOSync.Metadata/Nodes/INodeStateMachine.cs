namespace MSOSync.Metadata.Nodes;

public interface INodeStateMachine
{
    Task TransitionAsync(string nodeId, string targetStatus, CancellationToken ct = default);
}
