# Task 4: MSOSync.Routing

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Routing/RouteCacheState.cs`
- Create: `src/MSOSync.Routing/IRoutingService.cs`
- Create: `src/MSOSync.Routing/RoutingService.cs`
- Create: `src/MSOSync.Routing/RoutingServiceExtensions.cs`
- Delete: `src/MSOSync.Routing/Placeholder.cs`

**Interfaces:**
- Consumes: `AppDbContext.TriggerRouters`, `AppDbContext.Routers`, `AppDbContext.Nodes`; MediatR events: `TriggerMetadataChangedEvent(TriggerId, Action)`, `RouterMetadataChangedEvent(RouterId, Action)`, `ChannelMetadataChangedEvent(ChannelId, Action)` all from `MSOSync.Metadata.Events`
- Produces:
  - `IRoutingService.ResolveAsync(string triggerId, CancellationToken)` → `IReadOnlyList<string>` (targetNodeIds)
  - `RouteCacheState` — singleton; holds shared `CancellationTokenSource` for route-wide cache invalidation
  - `AddRoutingServices(IServiceCollection)` extension

**Cache design:** key `routing:trigger:{triggerId}`, 60 s absolute TTL + `CancellationChangeToken` from `RouteCacheState._cts`. Trigger change → evict that key. Router/channel change → cancel CTS (evicts all routing entries without touching other `IMemoryCache` entries).

---

- [ ] **Step 1: Create `RouteCacheState`**

```csharp
// src/MSOSync.Routing/RouteCacheState.cs
namespace MSOSync.Routing;

internal sealed class RouteCacheState
{
    private CancellationTokenSource _cts = new();

    public CancellationToken CurrentToken => _cts.Token;

    public void InvalidateAll()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}
```

- [ ] **Step 2: Create `IRoutingService`**

```csharp
// src/MSOSync.Routing/IRoutingService.cs
namespace MSOSync.Routing;

public interface IRoutingService
{
    Task<IReadOnlyList<string>> ResolveAsync(string triggerId, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `RoutingService`**

`RoutingService` resolves `triggerId → [targetNodeIds]` via a join: `TriggerRouter → Router → Nodes in TargetNodeGroup`.
It also handles all three cache-invalidation notifications.

```csharp
// src/MSOSync.Routing/RoutingService.cs
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
```

- [ ] **Step 4: Create `RoutingServiceExtensions`**

`IMemoryCache` may already be registered by `AddMetadata`; `AddMemoryCache()` is idempotent.
MediatR is already registered by Metadata module; we register handlers from this assembly additionally.

```csharp
// src/MSOSync.Routing/RoutingServiceExtensions.cs
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Routing;

public static class RoutingServiceExtensions
{
    public static IServiceCollection AddRoutingServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<RoutingService>());
        services.AddSingleton<RouteCacheState>();
        services.AddScoped<IRoutingService, RoutingService>();
        return services;
    }
}
```

- [ ] **Step 5: Delete `Placeholder.cs`**

```pwsh
Remove-Item src/MSOSync.Routing/Placeholder.cs
```

- [ ] **Step 6: Build**

```pwsh
dotnet build src/MSOSync.Routing/MSOSync.Routing.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```pwsh
git add src/MSOSync.Routing/RouteCacheState.cs `
        src/MSOSync.Routing/IRoutingService.cs `
        src/MSOSync.Routing/RoutingService.cs `
        src/MSOSync.Routing/RoutingServiceExtensions.cs
git rm src/MSOSync.Routing/Placeholder.cs
git commit -m "feat(routing): add RoutingService with selective IMemoryCache invalidation"
```
