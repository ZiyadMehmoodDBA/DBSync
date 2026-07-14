// src/MSOSync.Metadata/NodeManagement/NodeScopeService.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed class NodeScopeService(AppDbContext db) : INodeScopeService
{
    public async Task<NodeScopeDto?> GetScopeAsync(string nodeId, CancellationToken ct = default)
    {
        var scope = await db.NodeScopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.NodeId == nodeId, ct);
        if (scope is null) return null;

        var channelIds = await db.NodeChannelAssignments.AsNoTracking()
            .Where(x => x.NodeId == nodeId)
            .Select(x => x.ChannelId)
            .ToArrayAsync(ct);

        var triggerIds = await db.NodeTriggerAssignments.AsNoTracking()
            .Where(x => x.NodeId == nodeId)
            .Select(x => x.TriggerId)
            .ToArrayAsync(ct);

        var routerIds = await db.NodeRouterAssignments.AsNoTracking()
            .Where(x => x.NodeId == nodeId)
            .Select(x => x.RouterId)
            .ToArrayAsync(ct);

        return new NodeScopeDto(nodeId, scope.SyncDirection, scope.InitialLoadPolicy,
            channelIds, triggerIds, routerIds);
    }

    public async Task<NodeScopeDto> SetScopeAsync(
        string nodeId, SetNodeScopeRequest req, string actor, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var existing = await db.NodeScopes.FirstOrDefaultAsync(s => s.NodeId == nodeId, ct);
        if (existing is null)
        {
            db.NodeScopes.Add(new SyncNodeScope
            {
                NodeId            = nodeId,
                SyncDirection     = req.SyncDirection,
                InitialLoadPolicy = req.InitialLoadPolicy,
                CreatedTime       = now,
                UpdatedTime       = now,
            });
        }
        else
        {
            existing.SyncDirection     = req.SyncDirection;
            existing.InitialLoadPolicy = req.InitialLoadPolicy;
            existing.UpdatedTime       = now;
        }

        // Replace channel assignments
        var oldChannels = await db.NodeChannelAssignments.Where(x => x.NodeId == nodeId).ToListAsync(ct);
        db.NodeChannelAssignments.RemoveRange(oldChannels);
        db.NodeChannelAssignments.AddRange(req.ChannelIds.Select(id =>
            new SyncNodeChannelAssignment { NodeId = nodeId, ChannelId = id }));

        // Replace trigger assignments
        var oldTriggers = await db.NodeTriggerAssignments.Where(x => x.NodeId == nodeId).ToListAsync(ct);
        db.NodeTriggerAssignments.RemoveRange(oldTriggers);
        db.NodeTriggerAssignments.AddRange(req.TriggerIds.Select(id =>
            new SyncNodeTriggerAssignment { NodeId = nodeId, TriggerId = id }));

        // Replace router assignments
        var oldRouters = await db.NodeRouterAssignments.Where(x => x.NodeId == nodeId).ToListAsync(ct);
        db.NodeRouterAssignments.RemoveRange(oldRouters);
        db.NodeRouterAssignments.AddRange(req.RouterIds.Select(id =>
            new SyncNodeRouterAssignment { NodeId = nodeId, RouterId = id }));

        await db.SaveChangesAsync(ct);

        return new NodeScopeDto(nodeId, req.SyncDirection, req.InitialLoadPolicy,
            req.ChannelIds, req.TriggerIds, req.RouterIds);
    }
}
