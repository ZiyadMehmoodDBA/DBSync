using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;

namespace MSOSync.Routing;

public sealed class RoutingService(
    AppDbContext db,
    IMemoryCache cache,
    RouteCacheState cacheState)
    : IRoutingService,
      INotificationHandler<TriggerMetadataChangedEvent>,
      INotificationHandler<RouterMetadataChangedEvent>,
      INotificationHandler<ChannelMetadataChangedEvent>
{
    private static string CacheKey(string triggerId) => $"routing:trigger:{triggerId}";

    public async Task<IReadOnlyList<string>> ResolveAsync(string triggerId, CancellationToken ct = default)
    {
        var key = CacheKey(triggerId);
        if (cache.TryGetValue<IReadOnlyList<string>>(key, out var cached))
            return cached!;

        var nodeIds = await db.TriggerRouters
            .AsNoTracking()
            .Where(tr => tr.TriggerId == triggerId && tr.Enabled)
            .Join(db.Routers.Where(r => r.Enabled),
                  tr => tr.RouterId, r => r.RouterId,
                  (tr, r) => r.TargetNodeGroup)
            .Join(db.Nodes.Where(n => n.SyncEnabled),
                  group => group, n => n.GroupId,
                  (group, n) => n.NodeId)
            .Distinct()
            .ToListAsync(ct);

        var result = nodeIds.AsReadOnly();
        using var entry = cache.CreateEntry(key);
        entry.Value = result;
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
        entry.AddExpirationToken(new CancellationChangeToken(cacheState.CurrentToken));

        return result;
    }

    public Task Handle(TriggerMetadataChangedEvent notification, CancellationToken ct)
    {
        cache.Remove(CacheKey(notification.TriggerId));
        return Task.CompletedTask;
    }

    public Task Handle(RouterMetadataChangedEvent notification, CancellationToken ct)
    {
        cacheState.InvalidateAll();
        return Task.CompletedTask;
    }

    public Task Handle(ChannelMetadataChangedEvent notification, CancellationToken ct)
    {
        cacheState.InvalidateAll();
        return Task.CompletedTask;
    }
}
