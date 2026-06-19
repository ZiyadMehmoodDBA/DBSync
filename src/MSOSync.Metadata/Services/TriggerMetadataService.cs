using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Services;

public sealed class TriggerMetadataService(
    AppDbContext db,
    IMemoryCache cache,
    IMediator mediator) : ITriggerMetadataService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<TriggerDto>> GetTriggersAsync(CancellationToken ct = default)
    {
        var triggers = await db.Triggers.AsNoTracking().ToListAsync(ct);
        return triggers.Select(MapTrigger).ToList().AsReadOnly();
    }

    public async Task<TriggerDto?> GetTriggerAsync(string triggerId, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:trigger:{triggerId}";
        if (cache.TryGetValue<TriggerDto>(cacheKey, out var cached))
            return cached;

        var trigger = await db.Triggers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TriggerId == triggerId, ct);
        if (trigger == null) return null;

        var dto = MapTrigger(trigger);
        cache.Set(cacheKey, dto, CacheOptions);
        return dto;
    }

    public async Task<IReadOnlyList<TriggerDto>> GetTriggersForChannelAsync(string channelId, CancellationToken ct = default)
    {
        var triggers = await db.Triggers.AsNoTracking()
            .Where(t => t.ChannelId == channelId)
            .ToListAsync(ct);
        return triggers.Select(MapTrigger).ToList().AsReadOnly();
    }

    public async Task<TriggerDto> CreateTriggerAsync(CreateTriggerRequest req, CancellationToken ct = default)
    {
        if (await db.Triggers.AnyAsync(t => t.TriggerId == req.TriggerId, ct))
            throw new DuplicateEntityException($"Trigger '{req.TriggerId}' already exists", "DUPLICATE_TRIGGER");

        var trigger = new SyncTrigger
        {
            TriggerId = req.TriggerId,
            SourceTable = req.SourceTable,
            ChannelId = req.ChannelId,
            SyncOnInsert = req.SyncOnInsert,
            SyncOnUpdate = req.SyncOnUpdate,
            SyncOnDelete = req.SyncOnDelete,
            Enabled = true,
            TriggerVersion = 1
        };
        db.Triggers.Add(trigger);
        db.TriggerHists.Add(new SyncTriggerHist
        {
            TriggerId = trigger.TriggerId,
            DdlText = null,
            TriggerVersion = trigger.TriggerVersion,
            CreateTime = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await mediator.Publish(new TriggerMetadataChangedEvent(trigger.TriggerId, "CREATED"), ct);
        return MapTrigger(trigger);
    }

    public async Task<TriggerDto> UpdateTriggerAsync(string triggerId, UpdateTriggerRequest req, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FindAsync([triggerId], ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");

        trigger.SourceTable = req.SourceTable;
        trigger.ChannelId = req.ChannelId;
        trigger.SyncOnInsert = req.SyncOnInsert;
        trigger.SyncOnUpdate = req.SyncOnUpdate;
        trigger.SyncOnDelete = req.SyncOnDelete;
        trigger.TriggerVersion++;

        db.TriggerHists.Add(new SyncTriggerHist
        {
            TriggerId = trigger.TriggerId,
            DdlText = null,
            TriggerVersion = trigger.TriggerVersion,
            CreateTime = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:trigger:{triggerId}");
        await mediator.Publish(new TriggerMetadataChangedEvent(triggerId, "UPDATED"), ct);
        return MapTrigger(trigger);
    }

    public async Task DeleteTriggerAsync(string triggerId, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FindAsync([triggerId], ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");

        var links = await db.TriggerRouters
            .Where(tr => tr.TriggerId == triggerId)
            .ToListAsync(ct);
        db.TriggerRouters.RemoveRange(links);
        db.Triggers.Remove(trigger);

        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:trigger:{triggerId}");
        await mediator.Publish(new TriggerMetadataChangedEvent(triggerId, "DELETED"), ct);
    }

    public async Task EnableTriggerAsync(string triggerId, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FindAsync([triggerId], ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");

        trigger.Enabled = true;
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:trigger:{triggerId}");
        await mediator.Publish(new TriggerMetadataChangedEvent(triggerId, "ENABLED"), ct);
    }

    public async Task DisableTriggerAsync(string triggerId, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FindAsync([triggerId], ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");

        trigger.Enabled = false;
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:trigger:{triggerId}");
        await mediator.Publish(new TriggerMetadataChangedEvent(triggerId, "DISABLED"), ct);
    }

    public async Task<IReadOnlyList<TriggerRouterDto>> GetTriggerRoutersAsync(string triggerId, CancellationToken ct = default)
    {
        var links = await db.TriggerRouters.AsNoTracking()
            .Where(tr => tr.TriggerId == triggerId)
            .ToListAsync(ct);
        return links.Select(tr => new TriggerRouterDto(tr.TriggerId, tr.RouterId, tr.Enabled))
            .ToList().AsReadOnly();
    }

    public async Task AddTriggerRouterAsync(string triggerId, string routerId, CancellationToken ct = default)
    {
        db.TriggerRouters.Add(new SyncTriggerRouter
        {
            TriggerId = triggerId,
            RouterId = routerId,
            Enabled = true
        });
        await db.SaveChangesAsync(ct);
        await mediator.Publish(new TriggerMetadataChangedEvent(triggerId, "ROUTER_ADDED"), ct);
    }

    public async Task RemoveTriggerRouterAsync(string triggerId, string routerId, CancellationToken ct = default)
    {
        var link = await db.TriggerRouters
            .FirstOrDefaultAsync(tr => tr.TriggerId == triggerId && tr.RouterId == routerId, ct)
            ?? throw new NotFoundException($"Trigger-router link {triggerId}/{routerId} not found", "TRIGGER_ROUTER_NOT_FOUND");

        db.TriggerRouters.Remove(link);
        await db.SaveChangesAsync(ct);
        await mediator.Publish(new TriggerMetadataChangedEvent(triggerId, "ROUTER_REMOVED"), ct);
    }

    public async Task<IReadOnlyList<TriggerHistDto>> GetTriggerHistoryAsync(string triggerId, CancellationToken ct = default)
    {
        var history = await db.TriggerHists.AsNoTracking()
            .Where(h => h.TriggerId == triggerId)
            .OrderByDescending(h => h.CreateTime)
            .ToListAsync(ct);
        return history.Select(h => new TriggerHistDto(h.HistId, h.TriggerId, h.DdlText, h.TriggerVersion, h.CreateTime))
            .ToList().AsReadOnly();
    }

    private static TriggerDto MapTrigger(SyncTrigger t) =>
        new(t.TriggerId, t.SourceTable, t.ChannelId,
            t.SyncOnInsert, t.SyncOnUpdate, t.SyncOnDelete,
            t.Enabled, t.TriggerVersion, t.LastVerifiedTime);
}
