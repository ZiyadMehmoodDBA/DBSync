# Epic 4 / Task 7: RouterMetadataService + ChannelMetadataService + DTOs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create all router and channel DTOs, events, interfaces, and full service implementations. Both services follow the same cache-invalidate-publish pattern as the other metadata services.

**Architecture:** `RouterMetadataService` filters by `SourceNodeGroup` or `TargetNodeGroup` for the two filtered list endpoints. `ChannelMetadataService` enforces duplicate check on create. Neither service writes history rows (no history tables for routers or channels). Cache key format: `"metadata:router:{routerId}"` and `"metadata:channel:{channelId}"`.

**Tech Stack:** EF Core 9.0.0, MediatR 12.4.1, `Microsoft.Extensions.Caching.Memory` 9.0.0

## Global Constraints

- DbSet names: `db.Routers`, `db.Channels` (existing names in `AppDbContext`)
- Cache key format: `"metadata:router:{routerId}"`, `"metadata:channel:{channelId}"`
- Cache expiry: absolute 60 seconds (`AbsoluteExpirationRelativeToNow`)
- All DTOs in `MSOSync.Metadata.Dtos` namespace
- Valid `RouterType` values: `"default"`, `"column"`, `"subselect"` (enforced in validator Task 9, not service)
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Metadata/Dtos/RouterDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/CreateRouterRequest.cs`
- Create: `src/MSOSync.Metadata/Dtos/UpdateRouterRequest.cs`
- Create: `src/MSOSync.Metadata/Dtos/ChannelDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/CreateChannelRequest.cs`
- Create: `src/MSOSync.Metadata/Dtos/UpdateChannelRequest.cs`
- Create: `src/MSOSync.Metadata/Events/RouterMetadataChangedEvent.cs`
- Create: `src/MSOSync.Metadata/Events/ChannelMetadataChangedEvent.cs`
- Create: `src/MSOSync.Metadata/Interfaces/IRouterMetadataService.cs`
- Create: `src/MSOSync.Metadata/Interfaces/IChannelMetadataService.cs`
- Create: `src/MSOSync.Metadata/Services/RouterMetadataService.cs`
- Create: `src/MSOSync.Metadata/Services/ChannelMetadataService.cs`

**Interfaces:**
- Consumes: `AppDbContext.Routers`, `AppDbContext.Channels`, `DuplicateEntityException`/`NotFoundException` (Task 3), `IMemoryCache`, `IMediator`
- Produces: `IRouterMetadataService`, `IChannelMetadataService` — consumed by Tasks 8 and 9

---

- [ ] **Step 1: Create Router DTO types**

```csharp
// src/MSOSync.Metadata/Dtos/RouterDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record RouterDto(
    string RouterId,
    string SourceNodeGroup,
    string TargetNodeGroup,
    string RouterType,
    bool Enabled);
```

```csharp
// src/MSOSync.Metadata/Dtos/CreateRouterRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record CreateRouterRequest(
    string RouterId,
    string SourceNodeGroup,
    string TargetNodeGroup,
    string RouterType = "default");
```

```csharp
// src/MSOSync.Metadata/Dtos/UpdateRouterRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record UpdateRouterRequest(
    string SourceNodeGroup,
    string TargetNodeGroup,
    string RouterType);
```

- [ ] **Step 2: Create Channel DTO types**

```csharp
// src/MSOSync.Metadata/Dtos/ChannelDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record ChannelDto(
    string ChannelId,
    int Priority,
    int BatchSize,
    int MaxBatchToSend,
    long MaxDataSize,
    bool Enabled);
```

```csharp
// src/MSOSync.Metadata/Dtos/CreateChannelRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record CreateChannelRequest(
    string ChannelId,
    int Priority,
    int BatchSize = 1000,
    int MaxBatchToSend = 10,
    long MaxDataSize = 1048576L);
```

```csharp
// src/MSOSync.Metadata/Dtos/UpdateChannelRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record UpdateChannelRequest(
    int Priority,
    int BatchSize,
    int MaxBatchToSend,
    long MaxDataSize);
```

- [ ] **Step 3: Create events**

```csharp
// src/MSOSync.Metadata/Events/RouterMetadataChangedEvent.cs
using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record RouterMetadataChangedEvent(
    string RouterId,
    string Action) : INotification;
// Action values: "CREATED" | "UPDATED" | "DELETED"
```

```csharp
// src/MSOSync.Metadata/Events/ChannelMetadataChangedEvent.cs
using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record ChannelMetadataChangedEvent(
    string ChannelId,
    string Action) : INotification;
// Action values: "CREATED" | "UPDATED" | "DELETED"
```

- [ ] **Step 4: Create `IRouterMetadataService` interface**

```csharp
// src/MSOSync.Metadata/Interfaces/IRouterMetadataService.cs
using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface IRouterMetadataService
{
    Task<IReadOnlyList<RouterDto>> GetRoutersAsync(CancellationToken ct = default);
    Task<RouterDto?> GetRouterAsync(string routerId, CancellationToken ct = default);
    Task<IReadOnlyList<RouterDto>> GetRoutersForSourceGroupAsync(string groupId, CancellationToken ct = default);
    Task<IReadOnlyList<RouterDto>> GetRoutersForTargetGroupAsync(string groupId, CancellationToken ct = default);
    Task<RouterDto> CreateRouterAsync(CreateRouterRequest req, CancellationToken ct = default);
    Task<RouterDto> UpdateRouterAsync(string routerId, UpdateRouterRequest req, CancellationToken ct = default);
    Task DeleteRouterAsync(string routerId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create `IChannelMetadataService` interface**

```csharp
// src/MSOSync.Metadata/Interfaces/IChannelMetadataService.cs
using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface IChannelMetadataService
{
    Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(CancellationToken ct = default);
    Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken ct = default);
    Task<ChannelDto> CreateChannelAsync(CreateChannelRequest req, CancellationToken ct = default);
    Task<ChannelDto> UpdateChannelAsync(string channelId, UpdateChannelRequest req, CancellationToken ct = default);
    Task DeleteChannelAsync(string channelId, CancellationToken ct = default);
}
```

- [ ] **Step 6: Create `RouterMetadataService` implementation**

```csharp
// src/MSOSync.Metadata/Services/RouterMetadataService.cs
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

public sealed class RouterMetadataService(
    AppDbContext db,
    IMemoryCache cache,
    IMediator mediator) : IRouterMetadataService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<RouterDto>> GetRoutersAsync(CancellationToken ct = default)
    {
        var routers = await db.Routers.AsNoTracking().ToListAsync(ct);
        return routers.Select(MapRouter).ToList().AsReadOnly();
    }

    public async Task<RouterDto?> GetRouterAsync(string routerId, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:router:{routerId}";
        if (cache.TryGetValue<RouterDto>(cacheKey, out var cached))
            return cached;

        var router = await db.Routers.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RouterId == routerId, ct);
        if (router == null) return null;

        var dto = MapRouter(router);
        cache.Set(cacheKey, dto, CacheOptions);
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
        cache.Remove($"metadata:router:{routerId}");
        await mediator.Publish(new RouterMetadataChangedEvent(routerId, "UPDATED"), ct);
        return MapRouter(router);
    }

    public async Task DeleteRouterAsync(string routerId, CancellationToken ct = default)
    {
        var router = await db.Routers.FindAsync([routerId], ct)
            ?? throw new NotFoundException($"Router '{routerId}' not found", "ROUTER_NOT_FOUND");

        db.Routers.Remove(router);
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:router:{routerId}");
        await mediator.Publish(new RouterMetadataChangedEvent(routerId, "DELETED"), ct);
    }

    private static RouterDto MapRouter(SyncRouter r) =>
        new(r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup, r.RouterType, r.Enabled);
}
```

- [ ] **Step 7: Create `ChannelMetadataService` implementation**

```csharp
// src/MSOSync.Metadata/Services/ChannelMetadataService.cs
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

public sealed class ChannelMetadataService(
    AppDbContext db,
    IMemoryCache cache,
    IMediator mediator) : IChannelMetadataService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(CancellationToken ct = default)
    {
        var channels = await db.Channels.AsNoTracking().ToListAsync(ct);
        return channels.Select(MapChannel).ToList().AsReadOnly();
    }

    public async Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:channel:{channelId}";
        if (cache.TryGetValue<ChannelDto>(cacheKey, out var cached))
            return cached;

        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (channel == null) return null;

        var dto = MapChannel(channel);
        cache.Set(cacheKey, dto, CacheOptions);
        return dto;
    }

    public async Task<ChannelDto> CreateChannelAsync(CreateChannelRequest req, CancellationToken ct = default)
    {
        if (await db.Channels.AnyAsync(c => c.ChannelId == req.ChannelId, ct))
            throw new DuplicateEntityException($"Channel '{req.ChannelId}' already exists", "DUPLICATE_CHANNEL");

        var channel = new SyncChannel
        {
            ChannelId = req.ChannelId,
            Priority = req.Priority,
            BatchSize = req.BatchSize,
            MaxBatchToSend = req.MaxBatchToSend,
            MaxDataSize = req.MaxDataSize,
            Enabled = true
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);
        await mediator.Publish(new ChannelMetadataChangedEvent(channel.ChannelId, "CREATED"), ct);
        return MapChannel(channel);
    }

    public async Task<ChannelDto> UpdateChannelAsync(string channelId, UpdateChannelRequest req, CancellationToken ct = default)
    {
        var channel = await db.Channels.FindAsync([channelId], ct)
            ?? throw new NotFoundException($"Channel '{channelId}' not found", "CHANNEL_NOT_FOUND");

        channel.Priority = req.Priority;
        channel.BatchSize = req.BatchSize;
        channel.MaxBatchToSend = req.MaxBatchToSend;
        channel.MaxDataSize = req.MaxDataSize;

        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:channel:{channelId}");
        await mediator.Publish(new ChannelMetadataChangedEvent(channelId, "UPDATED"), ct);
        return MapChannel(channel);
    }

    public async Task DeleteChannelAsync(string channelId, CancellationToken ct = default)
    {
        var channel = await db.Channels.FindAsync([channelId], ct)
            ?? throw new NotFoundException($"Channel '{channelId}' not found", "CHANNEL_NOT_FOUND");

        db.Channels.Remove(channel);
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:channel:{channelId}");
        await mediator.Publish(new ChannelMetadataChangedEvent(channelId, "DELETED"), ct);
    }

    private static ChannelDto MapChannel(SyncChannel c) =>
        new(c.ChannelId, c.Priority, c.BatchSize, c.MaxBatchToSend, c.MaxDataSize, c.Enabled);
}
```

- [ ] **Step 8: Build `MSOSync.Metadata` to verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build src\MSOSync.Metadata\MSOSync.Metadata.csproj
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 9: Build full solution**

```powershell
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 10: Commit**

```powershell
git add src/MSOSync.Metadata/Dtos/RouterDto.cs
git add src/MSOSync.Metadata/Dtos/CreateRouterRequest.cs
git add src/MSOSync.Metadata/Dtos/UpdateRouterRequest.cs
git add src/MSOSync.Metadata/Dtos/ChannelDto.cs
git add src/MSOSync.Metadata/Dtos/CreateChannelRequest.cs
git add src/MSOSync.Metadata/Dtos/UpdateChannelRequest.cs
git add src/MSOSync.Metadata/Events/RouterMetadataChangedEvent.cs
git add src/MSOSync.Metadata/Events/ChannelMetadataChangedEvent.cs
git add src/MSOSync.Metadata/Interfaces/IRouterMetadataService.cs
git add src/MSOSync.Metadata/Interfaces/IChannelMetadataService.cs
git add src/MSOSync.Metadata/Services/RouterMetadataService.cs
git add src/MSOSync.Metadata/Services/ChannelMetadataService.cs
git commit -m "feat(metadata): add RouterMetadataService and ChannelMetadataService"
```
