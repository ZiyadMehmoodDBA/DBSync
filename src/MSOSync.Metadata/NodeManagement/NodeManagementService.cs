using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Pagination;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using System.Text.Json;

namespace MSOSync.Metadata.NodeManagement;

public sealed class NodeManagementService(
    AppDbContext             db,
    IRegistrationDiffService diff) : INodeManagementService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<CursorPageResult<RegistrationSummaryDto>> GetRegistrationsAsync(
        RegistrationFilter filter, CancellationToken ct = default)
    {
        var q = db.RegistrationRequests.AsNoTracking();

        if (filter.Status           is not null) q = q.Where(e => e.Status           == filter.Status.Value);
        if (filter.RegistrationType is not null) q = q.Where(e => e.RegistrationType == filter.RegistrationType.Value);

        // Cursor: decode as (RequestId, ticks) — order descending by RequestId
        if (filter.Cursor is not null)
        {
            try
            {
                var (cursorId, _) = CursorToken.Decode(filter.Cursor);
                q = q.Where(e => e.RequestId < cursorId);
            }
            catch (ArgumentException) { /* invalid cursor — ignore */ }
        }

        int? total = null;
        if (filter.IncludeTotalCount)
            total = await q.CountAsync(ct);

        var pageSize = Math.Clamp(filter.PageSize, 1, 500);
        var items    = await q
            .OrderByDescending(e => e.RequestId)
            .Take(pageSize + 1)
            .Select(e => new RegistrationSummaryDto(
                e.RequestId, e.NodeId, e.NodeName,
                e.RegistrationType, e.Status,
                e.RequestTime ?? DateTime.UtcNow, e.ProcessedAt, e.ProcessedBy))
            .ToListAsync(ct);

        var hasMore  = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = CursorToken.Encode(last.Id, last.ReceivedAt.Ticks);
        }

        return new CursorPageResult<RegistrationSummaryDto>(
            items.AsReadOnly(), nextCursor, hasMore, total);
    }

    public async Task<RegistrationDetailDto?> GetRegistrationByIdAsync(
        long id, CancellationToken ct = default)
    {
        var e = await db.RegistrationRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RequestId == id, ct);
        if (e is null) return null;

        RegistrationMetadataDto? metadata = null;
        if (e.MetadataJson is not null)
        {
            try { metadata = JsonSerializer.Deserialize<RegistrationMetadataDto>(e.MetadataJson, JsonOpts); }
            catch { /* corrupt metadata — return null */ }
        }

        RegistrationDiffDto? diffDto = null;
        if (e.RegistrationType != RegistrationType.New && metadata is not null)
        {
            var node = await db.Nodes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.NodeId == e.NodeId, ct);
            if (node is not null)
                diffDto = diff.Compute(metadata, node);
        }

        return new RegistrationDetailDto(
            e.RequestId, e.NodeId, e.NodeName, e.RegistrationType, e.Status,
            e.RequestTime ?? DateTime.UtcNow, e.ProcessedAt, e.ProcessedBy,
            metadata, diffDto);
    }

    public async Task<NodeManagementOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var pendingAll = await db.RegistrationRequests.AsNoTracking()
            .CountAsync(r => r.Status == RegistrationStatus.Pending, ct);
        var pendingRec = await db.RegistrationRequests.AsNoTracking()
            .CountAsync(r => r.Status == RegistrationStatus.Pending
                          && r.RegistrationType == RegistrationType.Recovery, ct);

        var nodes       = await db.Nodes.AsNoTracking().ToListAsync(ct);
        var totalGroups = await db.NodeGroups.AsNoTracking().CountAsync(ct);

        var lastReg = await db.RegistrationRequests.AsNoTracking()
            .OrderByDescending(r => r.RequestTime)
            .Select(r => r.RequestTime)
            .FirstOrDefaultAsync(ct);

        var lastApproval = await db.RegistrationRequests.AsNoTracking()
            .Where(r => r.Status == RegistrationStatus.Approved)
            .OrderByDescending(r => r.ProcessedAt)
            .Select(r => r.ProcessedAt)
            .FirstOrDefaultAsync(ct);

        return new NodeManagementOverviewDto(
            PendingRegistrations: pendingAll,
            PendingRecoveries:    pendingRec,
            TotalNodes:           nodes.Count,
            ActiveNodes:          nodes.Count(n => n.LifecycleState == NodeLifecycleState.Active),
            OfflineNodes:         nodes.Count(n => n.ConnectivityStatus == ConnectivityStatus.Unreachable),
            DegradedNodes:        nodes.Count(n => n.ConnectivityStatus == ConnectivityStatus.Degraded),
            TotalGroups:          totalGroups,
            LastRegistrationAt:   lastReg,
            LastApprovalAt:       lastApproval,
            GeneratedAt:          DateTime.UtcNow);
    }
}
