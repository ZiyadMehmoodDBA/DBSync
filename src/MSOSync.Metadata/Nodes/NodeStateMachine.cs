using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Nodes;

public sealed class NodeStateMachine(AppDbContext db) : INodeStateMachine
{
    private static readonly IReadOnlySet<string> AutomaticTargets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "REGISTERED", "OFFLINE" };

    private static readonly IReadOnlySet<string> ProtectedStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DISABLED", "PENDING" };

    public async Task TransitionAsync(string nodeId, string targetStatus, CancellationToken ct = default)
    {
        if (!AutomaticTargets.Contains(targetStatus))
            throw new InvalidOperationException(
                $"Target status '{targetStatus}' is not a valid automatic transition. " +
                "Only REGISTERED and OFFLINE are managed automatically.");

        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
            ?? throw new InvalidOperationException($"Node {nodeId} not found");

        if (ProtectedStatuses.Contains(node.Status))
            throw new InvalidOperationException(
                $"Node '{nodeId}' has status '{node.Status}' (DISABLED or PENDING) and cannot be changed automatically.");

        node.Status = targetStatus.ToUpperInvariant();
        await db.SaveChangesAsync(ct);
    }
}
