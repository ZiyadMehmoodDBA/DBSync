using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Topology;

public sealed class TopologyQueryService(AppDbContext db, IMemoryCache cache)
    : ITopologyQueryService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    // Worst-of-members rule: Unreachable > Degraded > Unknown > Reachable; empty → Unknown
    private static ConnectivityStatus AggregateConnectivity(
        IReadOnlyList<ConnectivityStatus> statuses)
    {
        if (statuses.Count == 0) return ConnectivityStatus.Unknown;
        if (statuses.Any(s => s == ConnectivityStatus.Unreachable)) return ConnectivityStatus.Unreachable;
        if (statuses.Any(s => s == ConnectivityStatus.Degraded))    return ConnectivityStatus.Degraded;
        if (statuses.Any(s => s == ConnectivityStatus.Unknown))     return ConnectivityStatus.Unknown;
        return ConnectivityStatus.Reachable;
    }

    private static TopologyGroupDto BuildGroupDto(
        string groupId, string? name,
        IReadOnlyList<ConnectivityStatus> memberStatuses)
    {
        return new TopologyGroupDto(
            groupId, name,
            memberStatuses.Count,
            memberStatuses.Count(s => s == ConnectivityStatus.Reachable),
            memberStatuses.Count(s => s == ConnectivityStatus.Degraded),
            memberStatuses.Count(s => s == ConnectivityStatus.Unreachable),
            memberStatuses.Count(s => s == ConnectivityStatus.Unknown),
            AggregateConnectivity(memberStatuses));
    }

    // ── GetTopologyGraphAsync ─────────────────────────────────────────────────
    // 4 DB round-trips; result cached for 60 seconds under "topology:graph:v1"
    public async Task<TopologyGraphDto> GetTopologyGraphAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("topology:graph:v1", out TopologyGraphDto? cached))
            return cached!;

        // Round-trip 1: all node groups
        var groups = await db.NodeGroups.AsNoTracking()
            .Select(g => new { g.GroupId, g.GroupName })
            .ToListAsync(ct);

        // Round-trip 2: all nodes — connectivity status and group membership
        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.NodeId, n.GroupId, n.ConnectivityStatus })
            .ToListAsync(ct);

        // Round-trip 3: all routers
        var routers = await db.Routers.AsNoTracking()
            .Select(r => new { r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup, r.Enabled })
            .ToListAsync(ct);

        // Round-trip 4: TriggerRouter JOIN Trigger → RouterId + ChannelId pairs
        // Grouped in C# (not SQL) to avoid complex EF GroupBy translation issues
        var joinRows = await db.TriggerRouters.AsNoTracking()
            .Join(db.Triggers,
                  tr => tr.TriggerId,
                  t  => t.TriggerId,
                  (tr, t) => new { tr.RouterId, t.ChannelId })
            .ToListAsync(ct);

        var channelsByRouter = joinRows
            .GroupBy(x => x.RouterId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.ChannelId).Distinct().ToList());

        var nodesByGroup = nodes.GroupBy(n => n.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConnectivityStatus).ToList());

        var nodeDtos = groups.Select(g =>
        {
            var statuses = nodesByGroup.TryGetValue(g.GroupId, out var s)
                ? (IReadOnlyList<ConnectivityStatus>)s
                : [];
            return new TopologyNodeDto(
                g.GroupId, g.GroupName,
                statuses.Count,
                statuses.Count(cs => cs == ConnectivityStatus.Reachable),
                statuses.Count(cs => cs == ConnectivityStatus.Degraded),
                statuses.Count(cs => cs == ConnectivityStatus.Unreachable),
                statuses.Count(cs => cs == ConnectivityStatus.Unknown),
                AggregateConnectivity(statuses));
        }).ToList();

        var edgeDtos = routers.Select(r => new TopologyEdgeDto(
            r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup,
            channelsByRouter.TryGetValue(r.RouterId, out var ch) ? ch : [],
            r.Enabled)).ToList();

        var result = new TopologyGraphDto(
            nodeDtos, edgeDtos,
            new TopologyMetadataDto("dagre", "TB", 1, DateTime.UtcNow));

        cache.Set("topology:graph:v1", result, CacheOptions);
        return result;
    }

    // ── GetTopologySummaryAsync ───────────────────────────────────────────────
    public async Task<TopologySummaryDto> GetTopologySummaryAsync(CancellationToken ct)
    {
        int totalGroups = await db.NodeGroups.AsNoTracking().CountAsync(ct);

        var statuses = await db.Nodes.AsNoTracking()
            .Select(n => n.ConnectivityStatus)
            .ToListAsync(ct);

        return new TopologySummaryDto(
            totalGroups,
            statuses.Count,
            statuses.Count(s => s == ConnectivityStatus.Reachable),
            statuses.Count(s => s == ConnectivityStatus.Degraded),
            statuses.Count(s => s == ConnectivityStatus.Unreachable),
            statuses.Count(s => s == ConnectivityStatus.Unknown),
            DateTime.UtcNow);
    }

    // ── GetGroupsAsync ────────────────────────────────────────────────────────
    // Result cached for 60 seconds under "topology:groups:v1"
    public async Task<IReadOnlyList<TopologyGroupDto>> GetGroupsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("topology:groups:v1", out IReadOnlyList<TopologyGroupDto>? cached))
            return cached!;

        var groups = await db.NodeGroups.AsNoTracking()
            .Select(g => new { g.GroupId, g.GroupName })
            .ToListAsync(ct);

        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.GroupId, n.ConnectivityStatus })
            .ToListAsync(ct);

        var nodesByGroup = nodes.GroupBy(n => n.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConnectivityStatus).ToList());

        var result = groups.Select(g =>
        {
            var statuses = nodesByGroup.TryGetValue(g.GroupId, out var s)
                ? (IReadOnlyList<ConnectivityStatus>)s
                : [];
            return BuildGroupDto(g.GroupId, g.GroupName, statuses);
        }).ToList();

        cache.Set("topology:groups:v1", (IReadOnlyList<TopologyGroupDto>)result, CacheOptions);
        return result;
    }

    // ── GetGroupAsync ─────────────────────────────────────────────────────────
    // Not cached — direct DB queries
    public async Task<TopologyGroupDto?> GetGroupAsync(string groupId, CancellationToken ct)
    {
        var group = await db.NodeGroups.AsNoTracking()
            .Where(g => g.GroupId == groupId)
            .Select(g => new { g.GroupId, g.GroupName })
            .FirstOrDefaultAsync(ct);

        if (group is null) return null;

        var statuses = await db.Nodes.AsNoTracking()
            .Where(n => n.GroupId == groupId)
            .Select(n => n.ConnectivityStatus)
            .ToListAsync(ct);

        return BuildGroupDto(group.GroupId, group.GroupName, statuses);
    }

    // ── GetGroupNodesAsync ────────────────────────────────────────────────────
    // Not cached — direct DB query; returns empty list for unknown groupId
    public async Task<IReadOnlyList<TopologyGroupNodeDto>> GetGroupNodesAsync(
        string groupId, CancellationToken ct)
    {
        var rows = await db.Nodes.AsNoTracking()
            .Where(n => n.GroupId == groupId)
            .Select(n => new
            {
                n.NodeId,
                n.Status,
                n.ConnectivityStatus,
                n.LastHeartbeat,
                n.LastProbeLatencyMs,
                n.SyncEnabled
            })
            .ToListAsync(ct);

        return rows.Select(n => new TopologyGroupNodeDto(
                n.NodeId,
                Enum.Parse<NodeStatus>(n.Status, ignoreCase: true),
                n.ConnectivityStatus,
                n.LastHeartbeat,
                n.LastProbeLatencyMs,
                n.SyncEnabled))
            .ToList();
    }
}
