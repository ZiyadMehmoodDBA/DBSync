# Task 3: Migrate Tier A Callers

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `IMemoryCache` with `ICacheService` in nine Metadata service files, update `Program.cs` to call `AddCacheService`, and update all affected unit tests.

**Prerequisite:** Tasks 1 and 2 complete.

**Files Modified:**
- `src/MSOSync.App/Program.cs`
- `src/MSOSync.Metadata/Overview/OverviewSnapshotCache.cs`
- `src/MSOSync.Metadata/Topology/TopologyQueryService.cs`
- `src/MSOSync.Metadata/Metrics/MetricsQueryService.cs`
- `src/MSOSync.Metadata/Permissions/PermissionService.cs`
- `src/MSOSync.Metadata/Services/NodeMetadataService.cs`
- `src/MSOSync.Metadata/Services/ChannelMetadataService.cs`
- `src/MSOSync.Metadata/Services/TriggerMetadataService.cs`
- `src/MSOSync.Metadata/Services/RouterMetadataService.cs`
- `src/MSOSync.Metadata/Services/ParameterMetadataService.cs`
- `tests/MSOSync.MetadataTests/Topology/TopologyQueryServiceTests.cs`
- `tests/MSOSync.MetadataTests/Metrics/MetricsQueryServiceTests.cs`
- `tests/MSOSync.MetadataTests/Permissions/PermissionServiceTests.cs`
- `tests/MSOSync.MetadataTests/ChannelMetadataServiceTests.cs`
- `tests/MSOSync.MetadataTests/RouterMetadataServiceTests.cs`
- `tests/MSOSync.MetadataTests/TriggerMetadataServiceTests.cs`
- `tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs`
- `tests/MSOSync.MetadataTests/ParameterMetadataServiceTests.cs`

**Interfaces:**
- Consumes: `ICacheService`, `CacheKeyHelper` from Task 1.
- `RoutingService` is NOT in scope — it retains `IMemoryCache`.

---

## Migration Pattern

Every Tier A service changes like this:

**Constructor (before):**
```csharp
public sealed class FooService(AppDbContext db, IMemoryCache cache, ...) : IFooService
```

**Constructor (after):**
```csharp
using MSOSync.Common.Caching;

public sealed class FooService(AppDbContext db, ICacheService cache, ...) : IFooService
```

**Get (before):**
```csharp
if (cache.TryGetValue("some:key:v1", out FooDto? cached))
    return cached!;
```

**Get (after):**
```csharp
var cached = await cache.GetAsync<FooDto>(CacheKeyHelper.FooKey(), ct);
if (cached is not null)
    return cached;
```

**Set (before):**
```csharp
cache.Set("some:key:v1", result, CacheOptions);
```

**Set (after):**
```csharp
await cache.SetAsync(CacheKeyHelper.FooKey(), result, TimeSpan.FromSeconds(N), ct);
```

**Remove (before):**
```csharp
cache.Remove($"some:key:{id}");
```

**Remove (after):**
```csharp
await cache.RemoveAsync(CacheKeyHelper.FooKey(id), ct);
```

Remove `private static readonly MemoryCacheEntryOptions CacheOptions = ...` and `private string CacheKey(...) = ...` fields from all migrated services (replaced by `CacheKeyHelper` calls).

---

## Steps

- [ ] **Step 1: Wire `AddCacheService` into `Program.cs`**

In `src/MSOSync.App/Program.cs`, find the line:

```csharp
builder.Services.AddMetadata(builder.Configuration);
```

Add the following line immediately before it:

```csharp
builder.Services.AddCacheService(builder.Configuration);
```

Also add the using at the top of the file (if not already imported via global usings):

```csharp
using MSOSync.Common.Caching;
```

The `AddMemoryCache()` call inside `MetadataServiceExtensions.AddMetadata` remains. It is idempotent and still required for `RoutingService`.

- [ ] **Step 2: Migrate `OverviewSnapshotCache`**

Replace `src/MSOSync.Metadata/Overview/OverviewSnapshotCache.cs` with:

```csharp
using MSOSync.Common.Caching;

namespace MSOSync.Metadata.Overview;

public sealed class OverviewSnapshotCache(ICacheService cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task InvalidateAsync(CancellationToken ct = default)
        => await cache.RemoveAsync(CacheKeyHelper.OverviewSnapshot(), ct);

    public async Task<OverviewDto> GetOrCreateAsync(
        Func<CancellationToken, Task<OverviewDto>> factory, CancellationToken ct)
    {
        var dto = await cache.GetAsync<OverviewDto>(CacheKeyHelper.OverviewSnapshot(), ct);
        if (dto is not null)
            return dto;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            dto = await cache.GetAsync<OverviewDto>(CacheKeyHelper.OverviewSnapshot(), ct);
            if (dto is not null)
                return dto;

            dto = await factory(ct);
            await cache.SetAsync(CacheKeyHelper.OverviewSnapshot(), dto, Ttl, ct);
            return dto;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
```

Note: The old `Invalidate()` method was synchronous. The new `InvalidateAsync` is async. Find all callers of the old `Invalidate()` and update them to `await cache.InvalidateAsync(ct)` — or change the method signature back to `public void Invalidate()` that calls `cache.RemoveAsync(...).GetAwaiter().GetResult()` if callers are synchronous contexts. Check usages with:

```bash
grep -rn "\.Invalidate()" src/MSOSync.Metadata/ --include="*.cs"
```

If callers are in async methods, use `await InvalidateAsync(ct)`. The `OverviewQueryService.cs` (the expected caller) should have a `CancellationToken` available. Update accordingly.

- [ ] **Step 3: Migrate `TopologyQueryService`**

Replace `src/MSOSync.Metadata/Topology/TopologyQueryService.cs` (only the cache-related parts). The complete migrated file:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Caching;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Topology;

public sealed class TopologyQueryService(AppDbContext db, ICacheService cache)
    : ITopologyQueryService
{
    private const int GraphTtlSeconds  = 60;
    private const int GroupsTtlSeconds = 60;

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
    public async Task<TopologyGraphDto> GetTopologyGraphAsync(CancellationToken ct)
    {
        var cached = await cache.GetAsync<TopologyGraphDto>(CacheKeyHelper.TopologyGraph(), ct);
        if (cached is not null) return cached;

        var groups = await db.NodeGroups.AsNoTracking()
            .Select(g => new { g.GroupId, g.GroupName })
            .ToListAsync(ct);

        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.GroupId, n.ConnectivityStatus })
            .ToListAsync(ct);

        var routers = await db.Routers.AsNoTracking()
            .Select(r => new { r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup, r.Enabled })
            .ToListAsync(ct);

        var joinRows = await db.TriggerRouters.AsNoTracking()
            .Join(db.Triggers,
                  tr => tr.TriggerId,
                  t  => t.TriggerId,
                  (tr, t) => new { tr.TriggerId, tr.RouterId, t.ChannelId })
            .ToListAsync(ct);

        var channelsByRouter = joinRows
            .GroupBy(x => x.RouterId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.ChannelId).Distinct().ToList());

        var routerSourceByRouterId = routers.ToDictionary(r => r.RouterId, r => r.SourceNodeGroup);
        var statsByGroup = joinRows
            .Where(x => routerSourceByRouterId.ContainsKey(x.RouterId))
            .GroupBy(x => routerSourceByRouterId[x.RouterId])
            .ToDictionary(
                g => g.Key,
                g => (TriggerCount: g.Select(x => x.TriggerId).Distinct().Count(),
                      ChannelCount: g.Select(x => x.ChannelId).Distinct().Count()));

        var nodesByGroup = nodes.GroupBy(n => n.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConnectivityStatus).ToList());

        var nodeDtos = groups.Select(g =>
        {
            var statuses = nodesByGroup.TryGetValue(g.GroupId, out var s)
                ? (IReadOnlyList<ConnectivityStatus>)s
                : [];
            var (trigCount, chanCount) = statsByGroup.TryGetValue(g.GroupId, out var gs)
                ? gs : (0, 0);
            return new TopologyGraphNodeDto(
                $"group:{g.GroupId}",
                g.GroupId,
                g.GroupName ?? g.GroupId,
                AggregateConnectivity(statuses),
                statuses.Count,
                trigCount,
                chanCount);
        }).ToList();

        var edgeDtos = routers.Select(r => new TopologyGraphEdgeDto(
            $"router:{r.RouterId}",
            $"group:{r.SourceNodeGroup}",
            $"group:{r.TargetNodeGroup}",
            channelsByRouter.TryGetValue(r.RouterId, out var ch) ? ch : [],
            r.Enabled)).ToList();

        int totalNodes  = nodeDtos.Sum(n => n.MemberCount);
        int onlineNodes = nodeDtos.Count(n => n.Status == ConnectivityStatus.Reachable);

        var result = new TopologyGraphDto(
            nodeDtos, edgeDtos,
            new TopologyGraphMetaDto(groups.Count, totalNodes, onlineNodes, DateTimeOffset.UtcNow));

        await cache.SetAsync(CacheKeyHelper.TopologyGraph(), result, TimeSpan.FromSeconds(GraphTtlSeconds), ct);
        return result;
    }

    // ── GetTopologySummaryAsync ───────────────────────────────────────────────
    public async Task<TopologySummaryDto> GetTopologySummaryAsync(CancellationToken ct)
    {
        int totalGroups = await db.NodeGroups.AsNoTracking().CountAsync(ct);

        var counts = await db.Nodes.AsNoTracking()
            .GroupBy(n => n.ConnectivityStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        int totalNodes  = counts.Values.Sum();
        int reachable   = counts.GetValueOrDefault(ConnectivityStatus.Reachable);
        int degraded    = counts.GetValueOrDefault(ConnectivityStatus.Degraded);
        int unreachable = counts.GetValueOrDefault(ConnectivityStatus.Unreachable);
        int unknown     = counts.GetValueOrDefault(ConnectivityStatus.Unknown);

        return new TopologySummaryDto(
            totalGroups, totalNodes, reachable, degraded, unreachable, unknown, DateTime.UtcNow);
    }

    // ── GetGroupsAsync ────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<TopologyGroupDto>> GetGroupsAsync(CancellationToken ct)
    {
        var cached = await cache.GetAsync<IReadOnlyList<TopologyGroupDto>>(CacheKeyHelper.TopologyGroups(), ct);
        if (cached is not null) return cached;

        var groups = await db.NodeGroups.AsNoTracking()
            .Select(g => new { g.GroupId, g.GroupName })
            .ToListAsync(ct);

        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.GroupId, n.ConnectivityStatus })
            .ToListAsync(ct);

        var nodesByGroup = nodes.GroupBy(n => n.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConnectivityStatus).ToList());

        var result = (IReadOnlyList<TopologyGroupDto>)groups.Select(g =>
        {
            var statuses = nodesByGroup.TryGetValue(g.GroupId, out var s)
                ? (IReadOnlyList<ConnectivityStatus>)s
                : [];
            return BuildGroupDto(g.GroupId, g.GroupName, statuses);
        }).ToList();

        await cache.SetAsync(CacheKeyHelper.TopologyGroups(), result, TimeSpan.FromSeconds(GroupsTtlSeconds), ct);
        return result;
    }

    // ── GetGroupAsync ─────────────────────────────────────────────────────────
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
    public async Task<IReadOnlyList<TopologyGroupNodeDto>> GetGroupNodesAsync(
        string groupId, CancellationToken ct)
    {
        var rows = await db.Nodes.AsNoTracking()
            .Where(n => n.GroupId == groupId)
            .Select(n => new
            {
                n.NodeId,
                n.LifecycleState,
                n.ConnectivityStatus,
                n.LastHeartbeat,
                n.LastProbeLatencyMs,
                n.MaintenanceMode
            })
            .ToListAsync(ct);

        return rows.Select(n =>
            new TopologyGroupNodeDto(
                n.NodeId,
                n.LifecycleState,
                n.ConnectivityStatus,
                n.LastHeartbeat,
                n.LastProbeLatencyMs,
                n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode)
        ).ToList();
    }
}
```

- [ ] **Step 4: Migrate `MetricsQueryService`**

Replace `src/MSOSync.Metadata/Metrics/MetricsQueryService.cs`. Change the constructor and all cache calls:

```csharp
// Constructor — change:
public sealed class MetricsQueryService(AppDbContext db, ICacheService cache)
    : IMetricsQueryService

// Remove: private static readonly MemoryCacheEntryOptions CacheOptions = ...

// In GetSummaryAsync — change the get and set:
var cached = await cache.GetAsync<MetricsSummaryDto>(CacheKeyHelper.MetricsSummary(), ct);
if (cached is not null) return cached;
// ...build result...
await cache.SetAsync(CacheKeyHelper.MetricsSummary(), result, TimeSpan.FromSeconds(30), ct);

// In GetNodeMetricsAsync — change the get and set:
var cached = await cache.GetAsync<IReadOnlyList<NodeMetricsDto>>(CacheKeyHelper.MetricsNodes(), ct);
if (cached is not null) return cached;
// ...build result...
await cache.SetAsync(CacheKeyHelper.MetricsNodes(), (IReadOnlyList<NodeMetricsDto>)result, TimeSpan.FromSeconds(30), ct);

// In GetChannelMetricsAsync — change the get and set:
var cached = await cache.GetAsync<IReadOnlyList<ChannelMetricsDto>>(CacheKeyHelper.MetricsChannels(), ct);
if (cached is not null) return cached;
// ...build result...
await cache.SetAsync(CacheKeyHelper.MetricsChannels(), (IReadOnlyList<ChannelMetricsDto>)result, TimeSpan.FromSeconds(30), ct);
```

Add `using MSOSync.Common.Caching;` at the top; remove `using Microsoft.Extensions.Caching.Memory;`.

- [ ] **Step 5: Migrate `PermissionService`**

In `src/MSOSync.Metadata/Permissions/PermissionService.cs`:

Change the constructor:
```csharp
// Before:
public sealed class PermissionService(AppDbContext db, IMemoryCache cache, IMediator mediator, ICurrentUserService currentUser)
// After:
public sealed class PermissionService(AppDbContext db, ICacheService cache, IMediator mediator, ICurrentUserService currentUser)
```

Remove `private static readonly MemoryCacheEntryOptions CacheOptions = ...` and `private string CacheKey(string roleName) => ...`.

Update `GetEffectivePermissionsAsync`:
```csharp
// Before (get):
if (cache.TryGetValue(CacheKey(roleName), out IReadOnlyList<string>? cached) && cached is not null)
    return new EffectivePermissionsDto(roleName, cached, DateTimeOffset.UtcNow);
// ...
cache.Set(CacheKey(roleName), (IReadOnlyList<string>)permissions, CacheOptions);

// After (get):
var cached = await cache.GetAsync<IReadOnlyList<string>>(CacheKeyHelper.Permissions(roleName), ct);
if (cached is not null)
    return new EffectivePermissionsDto(roleName, cached, DateTimeOffset.UtcNow);
// ...
await cache.SetAsync(CacheKeyHelper.Permissions(roleName), (IReadOnlyList<string>)permissions, TimeSpan.FromSeconds(60), ct);
```

Update all `cache.Remove(CacheKey(roleName))` calls:
```csharp
// Before:
cache.Remove(CacheKey(roleName));
// After:
await cache.RemoveAsync(CacheKeyHelper.Permissions(roleName), ct);
```

The four write methods (`GrantPermissionAsync`, `RevokePermissionAsync`, `ResetRoleToDefaultsAsync`, `CopyPermissionsFromAsync`) each call `cache.Remove`. Replace each with `await cache.RemoveAsync(CacheKeyHelper.Permissions(roleName), ct);` — or `CacheKeyHelper.Permissions(targetRole)` for `CopyPermissionsFromAsync`.

Add `using MSOSync.Common.Caching;`; remove `using Microsoft.Extensions.Caching.Memory;`.

- [ ] **Step 6: Migrate `NodeMetadataService`**

In `src/MSOSync.Metadata/Services/NodeMetadataService.cs`:

Change the constructor parameter `IMemoryCache cache` to `ICacheService cache`.

Remove `private static readonly MemoryCacheEntryOptions CacheOptions = ...`.

Update `GetNodeAsync`:
```csharp
// Before:
var cacheKey = $"metadata:node:{nodeId}";
if (cache.TryGetValue<NodeDto>(cacheKey, out var cached))
    return cached;
// ...
cache.Set(cacheKey, dto, CacheOptions);

// After:
var cached = await cache.GetAsync<NodeDto>(CacheKeyHelper.Node(nodeId), ct);
if (cached is not null) return cached;
// ...
await cache.SetAsync(CacheKeyHelper.Node(nodeId), dto, TimeSpan.FromSeconds(60), ct);
```

Update `UpdateNodeAsync` and `RecordHeartbeatAsync` (two `cache.Remove` calls):
```csharp
// Before:
cache.Remove($"metadata:node:{nodeId}");
// After:
await cache.RemoveAsync(CacheKeyHelper.Node(nodeId), ct);
```

Add `using MSOSync.Common.Caching;`; remove `using Microsoft.Extensions.Caching.Memory;`.

- [ ] **Step 7: Migrate `ChannelMetadataService`**

In `src/MSOSync.Metadata/Services/ChannelMetadataService.cs`:

Change constructor: `IMemoryCache cache` → `ICacheService cache`.

Remove `MemoryCacheEntryOptions CacheOptions`.

Update `GetChannelAsync`:
```csharp
// Before:
var cacheKey = $"metadata:channel:{channelId}";
if (cache.TryGetValue<ChannelDto>(cacheKey, out var cached))
    return cached;
// ...
cache.Set(cacheKey, dto, CacheOptions);

// After:
var cached = await cache.GetAsync<ChannelDto>(CacheKeyHelper.Channel(channelId), ct);
if (cached is not null) return cached;
// ...
await cache.SetAsync(CacheKeyHelper.Channel(channelId), dto, TimeSpan.FromSeconds(60), ct);
```

Update `UpdateChannelAsync` and `DeleteChannelAsync` (`cache.Remove` → `await cache.RemoveAsync`):
```csharp
await cache.RemoveAsync(CacheKeyHelper.Channel(channelId), ct);
```

Add `using MSOSync.Common.Caching;`; remove `using Microsoft.Extensions.Caching.Memory;`.

- [ ] **Step 8: Migrate `TriggerMetadataService`**

In `src/MSOSync.Metadata/Services/TriggerMetadataService.cs`:

Change constructor: `IMemoryCache cache` → `ICacheService cache`.

Remove `MemoryCacheEntryOptions CacheOptions`.

Update `GetTriggerAsync`:
```csharp
var cached = await cache.GetAsync<TriggerDto>(CacheKeyHelper.Trigger(triggerId), ct);
if (cached is not null) return cached;
// ...
await cache.SetAsync(CacheKeyHelper.Trigger(triggerId), dto, TimeSpan.FromSeconds(60), ct);
```

Update all `cache.Remove($"metadata:trigger:{triggerId}")` calls (in `UpdateTriggerAsync`, `DeleteTriggerAsync`, `EnableTriggerAsync`, `DisableTriggerAsync`):
```csharp
await cache.RemoveAsync(CacheKeyHelper.Trigger(triggerId), ct);
```

Add `using MSOSync.Common.Caching;`; remove `using Microsoft.Extensions.Caching.Memory;`.

- [ ] **Step 9: Migrate `RouterMetadataService`**

In `src/MSOSync.Metadata/Services/RouterMetadataService.cs`:

Change constructor: `IMemoryCache cache` → `ICacheService cache`.

Remove `MemoryCacheEntryOptions CacheOptions`.

Update `GetRouterAsync`:
```csharp
var cached = await cache.GetAsync<RouterDto>(CacheKeyHelper.Router(routerId), ct);
if (cached is not null) return cached;
// ...
await cache.SetAsync(CacheKeyHelper.Router(routerId), dto, TimeSpan.FromSeconds(60), ct);
```

Update `UpdateRouterAsync` and `DeleteRouterAsync` (`cache.Remove` calls):
```csharp
await cache.RemoveAsync(CacheKeyHelper.Router(routerId), ct);
```

Add `using MSOSync.Common.Caching;`; remove `using Microsoft.Extensions.Caching.Memory;`.

- [ ] **Step 10: Migrate `ParameterMetadataService`**

In `src/MSOSync.Metadata/Services/ParameterMetadataService.cs`:

Change constructor: `IMemoryCache cache` → `ICacheService cache`.

Remove `MemoryCacheEntryOptions CacheOptions`.

Update `GetParameterAsync`:
```csharp
var cached = await cache.GetAsync<ParameterDto>(CacheKeyHelper.Parameter(name), ct);
if (cached is not null) return cached;
// ...
await cache.SetAsync(CacheKeyHelper.Parameter(name), dto, TimeSpan.FromSeconds(60), ct);
```

Update `UpdateParameterAsync`:
```csharp
await cache.RemoveAsync(CacheKeyHelper.Parameter(name), ct);
```

Add `using MSOSync.Common.Caching;`; remove `using Microsoft.Extensions.Caching.Memory;`.

- [ ] **Step 11: Build to confirm no compilation errors**

```bash
dotnet build MSOSync.sln
```

Expected: Zero errors. The only warnings that may appear are CS8600 nullable warnings if any cast is needed — resolve them by checking the ICacheService generic signature.

- [ ] **Step 12: Update `TopologyQueryServiceTests`**

Open `tests/MSOSync.MetadataTests/Topology/TopologyQueryServiceTests.cs`.

The `Make` factory currently uses `new MemoryCache(...)`. Replace it with a `Mock<ICacheService>` that behaves as a real pass-through cache (using a backing dictionary), so existing test assertions still pass without mocking individual keys:

```csharp
// At the top of the file, add:
using Moq;
using MSOSync.Common.Caching;

// Replace the Make factory:
private static TopologyQueryService Make(out Microsoft.EntityFrameworkCore.DbContext db)
{
    var ctx   = TestDbContext.Create();
    db = ctx;
    var cache = BuildPassThroughCache();
    return new TopologyQueryService(ctx, cache);
}

private static ICacheService BuildPassThroughCache()
{
    // Simple in-process dictionary that mirrors ICacheService behaviour for tests
    var store = new System.Collections.Concurrent.ConcurrentDictionary<string, object?>();
    var mock  = new Mock<ICacheService>();

    mock.Setup(c => c.GetAsync<It.IsAnyType>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns((string key, CancellationToken _) =>
        {
            store.TryGetValue(key, out var v);
            return Task.FromResult((dynamic?)v);
        });

    mock.Setup(c => c.SetAsync(
            It.IsAny<string>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
        .Returns((string key, object? value, TimeSpan? _, CancellationToken _) =>
        {
            store[key] = value;
            return Task.CompletedTask;
        });

    mock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns((string key, CancellationToken _) =>
        {
            store.TryRemove(key, out _);
            return Task.CompletedTask;
        });

    return mock.Object;
}
```

**Simpler alternative:** Use `InMemoryCacheService` directly (it is `internal` but the test is in a different assembly — you may need `InternalsVisibleTo`). The cleanest approach for these integration-style service tests is to use a real `InMemoryCacheService`:

```csharp
// Add to MSOSync.Common.csproj or a test-specific assembly attribute:
// [assembly: InternalsVisibleTo("MSOSync.MetadataTests")]

// Then in the test:
private static TopologyQueryService Make(out Microsoft.EntityFrameworkCore.DbContext db)
{
    var ctx      = TestDbContext.Create();
    db           = ctx;
    var memCache = new MemoryCache(new MemoryCacheOptions());
    var opts     = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
    ICacheService cache = new InMemoryCacheService(memCache, opts);
    return new TopologyQueryService(ctx, cache);
}
```

Choose the `InMemoryCacheService` approach — it is simpler and requires adding one line to `MSOSync.Common.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="MSOSync.MetadataTests" />
  <InternalsVisibleTo Include="MSOSync.IntegrationTests" />
</ItemGroup>
```

Add this to `src/MSOSync.Common/MSOSync.Common.csproj`.

- [ ] **Step 13: Update `MetricsQueryServiceTests`**

Open `tests/MSOSync.MetadataTests/Metrics/MetricsQueryServiceTests.cs`.

Replace:
```csharp
private readonly IMemoryCache        _cache;
// ...
_cache = new MemoryCache(new MemoryCacheOptions());
_sut   = new MetricsQueryService(_db, _cache);
// ...
public void Dispose() { _db.Dispose(); _cache.Dispose(); }
```

With:
```csharp
private readonly MemoryCache         _memCache;
private readonly ICacheService       _cache;
// ...
_memCache = new MemoryCache(new MemoryCacheOptions());
var opts  = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
_cache    = new InMemoryCacheService(_memCache, opts);
_sut      = new MetricsQueryService(_db, _cache);
// ...
public void Dispose() { _db.Dispose(); _memCache.Dispose(); }
```

Add usings:
```csharp
using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
```

Remove: `using Microsoft.Extensions.Caching.Memory;` (keep `MemoryCache` and `MemoryCacheOptions` — needed for constructing `InMemoryCacheService`; actually keep the using).

- [ ] **Step 14: Update `PermissionServiceTests`**

Open `tests/MSOSync.MetadataTests/Permissions/PermissionServiceTests.cs`.

Replace:
```csharp
private readonly IMemoryCache               _cache;
// ...
_cache = new MemoryCache(new MemoryCacheOptions());
// ...
_sut = new PermissionService(_db, _cache, _mediator.Object, _currentUser.Object);
```

With:
```csharp
private readonly MemoryCache                _memCache;
private readonly ICacheService              _cache;
// ...
_memCache = new MemoryCache(new MemoryCacheOptions());
var cacheOpts = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
_cache    = new InMemoryCacheService(_memCache, cacheOpts);
// ...
_sut = new PermissionService(_db, _cache, _mediator.Object, _currentUser.Object);
```

There is one test that directly inspects `_cache.TryGetValue("permissions:VIEWER", out _)`. This test verifies cache eviction. Update it to use `GetAsync`:

```csharp
[Fact]
public async Task Grant_EvictsCacheForRole()
{
    // Prime the cache
    await _sut.GetEffectivePermissionsAsync("alice");
    var before = await _cache.GetAsync<IReadOnlyList<string>>(CacheKeyHelper.Permissions("VIEWER"));
    before.Should().NotBeNull(); // cache was primed

    await _sut.GrantPermissionAsync("VIEWER", "EXPORT_DATA");

    var after = await _cache.GetAsync<IReadOnlyList<string>>(CacheKeyHelper.Permissions("VIEWER"));
    after.Should().BeNull(); // cache was evicted
}
```

Add usings:
```csharp
using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
```

- [ ] **Step 15: Update remaining metadata test files**

For `ChannelMetadataServiceTests.cs`, `RouterMetadataServiceTests.cs`, `TriggerMetadataServiceTests.cs`, `NodeMetadataServiceTests.cs`, and `ParameterMetadataServiceTests.cs` — apply the same pattern: replace `IMemoryCache`/`MemoryCache` constructor fields with `ICacheService` backed by `InMemoryCacheService`.

First, inspect each test file to understand its current cache construction pattern:

```bash
grep -n "IMemoryCache\|MemoryCache\|new.*Cache" tests/MSOSync.MetadataTests/ChannelMetadataServiceTests.cs
grep -n "IMemoryCache\|MemoryCache\|new.*Cache" tests/MSOSync.MetadataTests/RouterMetadataServiceTests.cs
grep -n "IMemoryCache\|MemoryCache\|new.*Cache" tests/MSOSync.MetadataTests/TriggerMetadataServiceTests.cs
grep -n "IMemoryCache\|MemoryCache\|new.*Cache" tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs
grep -n "IMemoryCache\|MemoryCache\|new.*Cache" tests/MSOSync.MetadataTests/ParameterMetadataServiceTests.cs
```

For each file, apply the same substitution pattern:

```csharp
// Remove:
var cache = new MemoryCache(new MemoryCacheOptions());
// or:
private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

// Replace with:
var memCache  = new MemoryCache(new MemoryCacheOptions());
var cacheOpts = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
ICacheService cache = new InMemoryCacheService(memCache, cacheOpts);
```

Add `using Microsoft.Extensions.Options; using MSOSync.Common.Caching;` to each file.

- [ ] **Step 16: Run all MetadataTests**

```bash
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj -v normal
```

Expected: All tests pass. Zero regressions.

- [ ] **Step 17: Run full solution build and all test suites**

```bash
dotnet build MSOSync.sln
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj
dotnet test tests/MSOSync.ArchTests/MSOSync.ArchTests.csproj
```

Expected: All pass.

- [ ] **Step 18: Commit**

```bash
git add src/MSOSync.App/Program.cs
git add src/MSOSync.Metadata/Overview/OverviewSnapshotCache.cs
git add src/MSOSync.Metadata/Topology/TopologyQueryService.cs
git add src/MSOSync.Metadata/Metrics/MetricsQueryService.cs
git add src/MSOSync.Metadata/Permissions/PermissionService.cs
git add src/MSOSync.Metadata/Services/NodeMetadataService.cs
git add src/MSOSync.Metadata/Services/ChannelMetadataService.cs
git add src/MSOSync.Metadata/Services/TriggerMetadataService.cs
git add src/MSOSync.Metadata/Services/RouterMetadataService.cs
git add src/MSOSync.Metadata/Services/ParameterMetadataService.cs
git add src/MSOSync.Common/MSOSync.Common.csproj
git add tests/MSOSync.MetadataTests/Topology/TopologyQueryServiceTests.cs
git add tests/MSOSync.MetadataTests/Metrics/MetricsQueryServiceTests.cs
git add tests/MSOSync.MetadataTests/Permissions/PermissionServiceTests.cs
git add tests/MSOSync.MetadataTests/ChannelMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/RouterMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/TriggerMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/ParameterMetadataServiceTests.cs
git commit -m "feat(2D.1-T3): migrate 9 Metadata services from IMemoryCache to ICacheService"
```
