using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.Services;

public sealed class NodeMetadataService(
    AppDbContext db,
    IMemoryCache cache,
    IMediator mediator,
    NodeSecurityService nodeSecurity) : INodeMetadataService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<NodeDto>> GetNodesAsync(CancellationToken ct = default)
    {
        var nodes = await db.Nodes.AsNoTracking().ToListAsync(ct);
        return nodes.Select(MapNode).ToList().AsReadOnly();
    }

    public async Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:node:{nodeId}";
        if (cache.TryGetValue<NodeDto>(cacheKey, out var cached))
            return cached;

        var node = await db.Nodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node == null) return null;

        var dto = MapNode(node);
        cache.Set(cacheKey, dto, CacheOptions);
        return dto;
    }

    public async Task<IReadOnlyList<NodeGroupDto>> GetNodeGroupsAsync(CancellationToken ct = default)
    {
        var groups = await db.NodeGroups.AsNoTracking().ToListAsync(ct);
        return groups.Select(g => new NodeGroupDto(g.GroupId, g.GroupName)).ToList().AsReadOnly();
    }

    public async Task<NodeDto> UpdateNodeAsync(string nodeId, UpdateNodeRequest req, CancellationToken ct = default)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node '{nodeId}' not found", "NODE_NOT_FOUND");

        node.GroupId = req.GroupId;
        node.SyncUrl = req.SyncUrl;
        node.HeartbeatInterval = req.HeartbeatInterval;

        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:node:{nodeId}");
        await mediator.Publish(new NodeMetadataChangedEvent(nodeId, "UPDATED"), ct);
        return MapNode(node);
    }

    public async Task EnableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node '{nodeId}' not found", "NODE_NOT_FOUND");

        node.SyncEnabled = true;
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:node:{nodeId}");
        await mediator.Publish(new NodeMetadataChangedEvent(nodeId, "ENABLED"), ct);
    }

    public async Task DisableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node '{nodeId}' not found", "NODE_NOT_FOUND");

        node.SyncEnabled = false;
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:node:{nodeId}");
        await mediator.Publish(new NodeMetadataChangedEvent(nodeId, "DISABLED"), ct);
    }

    public async Task<IReadOnlyList<RegistrationRequestDto>> GetPendingRegistrationsAsync(CancellationToken ct = default)
    {
        var requests = await db.RegistrationRequests.AsNoTracking()
            .Where(r => !r.Approved)
            .ToListAsync(ct);
        return requests.Select(MapRegistration).ToList().AsReadOnly();
    }

    public async Task<NodeProvisionResult> ApproveRegistrationAsync(long requestId, CancellationToken ct = default)
    {
        var request = await db.RegistrationRequests.FindAsync([requestId], ct)
            ?? throw new NotFoundException($"Registration request {requestId} not found", "REGISTRATION_NOT_FOUND");

        if (request.Approved)
            throw new ValidationException($"Registration request {requestId} is already approved", "ALREADY_APPROVED");

        request.Approved = true;

        db.Nodes.Add(new SyncNode
        {
            NodeId = request.NodeId,
            GroupId = request.NodeGroup ?? "default",
            SyncUrl = request.SyncUrl ?? "http://localhost",
            Status = "APPROVED",
            RegistrationTime = DateTime.UtcNow
        });

        var result = nodeSecurity.PrepareToken(request.NodeId);

        await db.SaveChangesAsync(ct);
        await mediator.Publish(new NodeMetadataChangedEvent(request.NodeId, "APPROVED"), ct);
        return result;
    }

    public async Task RejectRegistrationAsync(long requestId, CancellationToken ct = default)
    {
        var request = await db.RegistrationRequests.FindAsync([requestId], ct)
            ?? throw new NotFoundException($"Registration request {requestId} not found", "REGISTRATION_NOT_FOUND");

        db.RegistrationRequests.Remove(request);
        await db.SaveChangesAsync(ct);
        await mediator.Publish(new NodeMetadataChangedEvent(request.NodeId, "REJECTED"), ct);
    }

    public async Task<NodeSecurityInfoDto> GetNodeSecurityInfoAsync(string nodeId, CancellationToken ct = default)
    {
        var sec = await db.NodeSecurities.AsNoTracking()
            .FirstOrDefaultAsync(s => s.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Security info for node '{nodeId}' not found", "NODE_SECURITY_NOT_FOUND");

        return new NodeSecurityInfoDto(
            sec.NodeId,
            sec.RotationScheduled.HasValue,
            sec.RotationScheduled,
            sec.CreatedTime);
    }

    public async Task RecordHeartbeatAsync(string nodeId, DateTime heartbeatTime, CancellationToken ct = default)
    {
        await db.Nodes
            .Where(n => n.NodeId == nodeId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LastHeartbeat, heartbeatTime), ct);
    }

    private static NodeDto MapNode(SyncNode n) =>
        new(n.NodeId, n.GroupId, n.SyncUrl, n.Status,
            n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval, n.SyncEnabled,
            n.TransportMode);

    private static RegistrationRequestDto MapRegistration(SyncRegistrationRequest r) =>
        new(r.RequestId, r.NodeId, r.NodeGroup, r.SyncUrl, r.NodeVersion, r.DbType, r.RequestTime, r.Approved);
}
