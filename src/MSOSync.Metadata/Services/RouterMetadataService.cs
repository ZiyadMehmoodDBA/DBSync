using MediatR;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Caching;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Services;

public sealed class RouterMetadataService(
    AppDbContext db,
    ICacheService cache,
    IMediator mediator) : IRouterMetadataService
{

    public async Task<IReadOnlyList<RouterDto>> GetRoutersAsync(CancellationToken ct = default)
    {
        var routers = await db.Routers.AsNoTracking().ToListAsync(ct);
        return routers.Select(MapRouter).ToList().AsReadOnly();
    }

    public async Task<RouterDto?> GetRouterAsync(string routerId, CancellationToken ct = default)
    {
        var cached = await cache.GetAsync<RouterDto>(CacheKeyHelper.Router(routerId), ct);
        if (cached is not null) return cached;

        var router = await db.Routers.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RouterId == routerId, ct);
        if (router == null) return null;

        var dto = MapRouter(router);
        await cache.SetAsync(CacheKeyHelper.Router(routerId), dto, TimeSpan.FromSeconds(60), ct);
        return dto;
    }

    public async Task<IReadOnlyList<RouterDto>> GetRoutersForSourceGroupAsync(string groupId, CancellationToken ct = default)
    {
        var routers = await db.Routers.AsNoTracking()
            .Where(r => r.SourceNodeGroup == groupId)
            .ToListAsync(ct);
        return routers.Select(MapRouter).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<RouterDto>> GetRoutersForTargetGroupAsync(string groupId, CancellationToken ct = default)
    {
        var routers = await db.Routers.AsNoTracking()
            .Where(r => r.TargetNodeGroup == groupId)
            .ToListAsync(ct);
        return routers.Select(MapRouter).ToList().AsReadOnly();
    }

    public async Task<RouterDto> CreateRouterAsync(CreateRouterRequest req, CancellationToken ct = default)
    {
        if (await db.Routers.AnyAsync(r => r.RouterId == req.RouterId, ct))
            throw new DuplicateEntityException($"Router '{req.RouterId}' already exists", "DUPLICATE_ROUTER");

        var router = new SyncRouter
        {
            RouterId = req.RouterId,
            SourceNodeGroup = req.SourceNodeGroup,
            TargetNodeGroup = req.TargetNodeGroup,
            RouterType = req.RouterType,
            Enabled = true
        };
        db.Routers.Add(router);
        await db.SaveChangesAsync(ct);
        await mediator.Publish(new RouterMetadataChangedEvent(router.RouterId, "CREATED"), ct);
        return MapRouter(router);
    }

    public async Task<RouterDto> UpdateRouterAsync(string routerId, UpdateRouterRequest req, CancellationToken ct = default)
    {
        var router = await db.Routers.FindAsync([routerId], ct)
            ?? throw new NotFoundException($"Router '{routerId}' not found", "ROUTER_NOT_FOUND");

        router.SourceNodeGroup = req.SourceNodeGroup;
        router.TargetNodeGroup = req.TargetNodeGroup;
        router.RouterType = req.RouterType;

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeyHelper.Router(routerId), ct);
        await mediator.Publish(new RouterMetadataChangedEvent(routerId, "UPDATED"), ct);
        return MapRouter(router);
    }

    public async Task DeleteRouterAsync(string routerId, CancellationToken ct = default)
    {
        var router = await db.Routers.FindAsync([routerId], ct)
            ?? throw new NotFoundException($"Router '{routerId}' not found", "ROUTER_NOT_FOUND");

        db.Routers.Remove(router);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeyHelper.Router(routerId), ct);
        await mediator.Publish(new RouterMetadataChangedEvent(routerId, "DELETED"), ct);
    }

    private static RouterDto MapRouter(SyncRouter r) =>
        new(r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup, r.RouterType, r.Enabled);
}
