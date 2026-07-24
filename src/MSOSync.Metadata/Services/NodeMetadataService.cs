using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Caching;
using MSOSync.Common.Exceptions;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.Services;

public sealed class NodeMetadataService(
    AppDbContext db,
    ICacheService cache,
    IMediator mediator,
    NodeSecurityService nodeSecurity,
    IDataProtectionProvider dataProtection,
    CursorSigner cursorSigner) : INodeMetadataService
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("NodeDbConnection");

    public async Task<IReadOnlyList<NodeDto>> GetNodesAsync(CancellationToken ct = default)
    {
        var nodes = await db.Nodes.AsNoTracking().ToListAsync(ct);
        return nodes.Select(MapNode).ToList().AsReadOnly();
    }

    public async Task<PagedResult<NodeDto>> GetNodesPagedAsync(
        int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var q = db.Nodes.AsNoTracking();
        var total = await q.CountAsync(ct);
        var nodes = await q
            .OrderBy(n => n.NodeId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var items = nodes.Select(MapNode).ToList().AsReadOnly();
        return new PagedResult<NodeDto>(items, pageNumber, pageSize, total);
    }

    public async Task<CursorPageResult<NodeDto>> GetNodesCursorAsync(
        NodeCursorFilter filter, CancellationToken ct = default)
    {
        var pageSize = filter.ClampedPageSize;
        var q = db.Nodes.AsNoTracking().OrderBy(n => n.NodeId);

        if (filter.Cursor is not null)
        {
            var (cursorNodeId, _) = cursorSigner.DecodeString(filter.Cursor);
            if (!string.IsNullOrEmpty(cursorNodeId))
            {
                q = (IOrderedQueryable<SyncNode>)q.Where(n => n.NodeId.CompareTo(cursorNodeId) > 0);
            }
            // empty sentinel → no filter, start from first page
        }

        var rows = await q
            .Take(pageSize + 1)
            .Select(n => new NodeDto(
                n.NodeId, n.GroupId, n.SyncUrl, n.LifecycleState,
                n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval,
                n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode,
                n.TransportMode, n.ConnectivityStatus, n.MaintenanceMode,
                n.DbServer, n.DbName, n.DbAuthMode, n.DbUser,
                n.DbPasswordEncrypted != null, n.AgentVersion))
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows = rows.Take(pageSize).ToList();

        string? nextCursor = hasMore
            ? cursorSigner.EncodeString(rows[^1].NodeId, DateTime.UtcNow.Ticks)
            : null;

        int? totalCount = null;
        if (filter.IncludeTotal)
            totalCount = await db.Nodes.AsNoTracking().CountAsync(ct);

        return new CursorPageResult<NodeDto>(rows.AsReadOnly(), nextCursor, hasMore, totalCount);
    }

    public async Task<NodeListGateResult> GetNodesWithGateAsync(
        int threshold, CancellationToken ct = default)
    {
        var count = await db.Nodes.AsNoTracking().CountAsync(ct);

        if (count < threshold)
        {
            var items = await db.Nodes.AsNoTracking()
                .OrderBy(n => n.NodeId)
                .Select(n => new NodeDto(
                    n.NodeId, n.GroupId, n.SyncUrl, n.LifecycleState,
                    n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval,
                    n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode,
                    n.TransportMode, n.ConnectivityStatus, n.MaintenanceMode,
                    n.DbServer, n.DbName, n.DbAuthMode, n.DbUser,
                    n.DbPasswordEncrypted != null, n.AgentVersion))
                .ToListAsync(ct);
            return new NodeListGateResult(false, items.AsReadOnly(), null);
        }

        // Exceeds threshold — encode a sentinel cursor so caller can start at /cursor page 1
        var firstCursor = cursorSigner.EncodeString(string.Empty, 0L);
        return new NodeListGateResult(true, null, firstCursor);
    }

    public async Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var cached = await cache.GetAsync<NodeDto>(CacheKeyHelper.Node(nodeId), ct);
        if (cached is not null) return cached;

        var node = await db.Nodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node == null) return null;

        var dto = MapNode(node);
        await cache.SetAsync(CacheKeyHelper.Node(nodeId), dto, TimeSpan.FromSeconds(60), ct);
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
        await cache.RemoveAsync(CacheKeyHelper.Node(nodeId), ct);
        await mediator.Publish(new NodeMetadataChangedEvent(nodeId, "UPDATED"), ct);
        return MapNode(node);
    }

    public async Task<IReadOnlyList<RegistrationRequestDto>> GetPendingRegistrationsAsync(CancellationToken ct = default)
    {
        var requests = await db.RegistrationRequests.AsNoTracking()
            .Where(r => !r.Approved)
            .ToListAsync(ct);
        return requests.Select(MapRegistration).ToList().AsReadOnly();
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
        await cache.RemoveAsync(CacheKeyHelper.Node(nodeId), ct);
    }

    public async Task<CreateNodeResult> CreateNodeAsync(CreateNodeRequest req, CancellationToken ct = default)
    {
        var exists = await db.Nodes.AnyAsync(n => n.NodeId == req.NodeId, ct);
        if (exists)
            throw new DuplicateEntityException($"Node '{req.NodeId}' already exists", "NODE_ALREADY_EXISTS");

        string? encryptedPassword = req.DbPassword != null
            ? _protector.Protect(req.DbPassword)
            : null;

        var node = new SyncNode
        {
            NodeId            = req.NodeId,
            GroupId           = req.GroupId,
            SyncUrl           = req.SyncUrl,
            LifecycleState    = NodeLifecycleState.PendingRegistration,  // spec §4.4: admin creating IS the approval
            RegistrationTime  = DateTime.UtcNow,
            HeartbeatInterval = req.HeartbeatInterval,
            TransportMode     = req.TransportMode,
            UpstreamNodeId    = req.UpstreamNodeId,
            DbServer          = req.DbServer,
            DbName            = req.DbName,
            DbAuthMode        = req.DbAuthMode,
            DbUser            = req.DbUser,
            DbPasswordEncrypted = encryptedPassword
        };

        db.Nodes.Add(node);

        var provision = nodeSecurity.PrepareToken(req.NodeId);

        await db.SaveChangesAsync(ct);
        await mediator.Publish(new NodeMetadataChangedEvent(req.NodeId, "CREATED"), ct);

        return new CreateNodeResult(req.NodeId, provision.RawToken, MapNode(node));
    }

    private static NodeDto MapNode(SyncNode n) =>
        new(n.NodeId, n.GroupId, n.SyncUrl, n.LifecycleState,
            n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval,
            n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode,
            n.TransportMode, n.ConnectivityStatus, n.MaintenanceMode,
            n.DbServer, n.DbName, n.DbAuthMode, n.DbUser,
            n.DbPasswordEncrypted != null, n.AgentVersion);

    private static RegistrationRequestDto MapRegistration(SyncRegistrationRequest r) =>
        new(r.RequestId, r.NodeId, r.NodeGroup, r.SyncUrl, r.NodeVersion, r.DbType, r.RequestTime, r.Approved);
}
