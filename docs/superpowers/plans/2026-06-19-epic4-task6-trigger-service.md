# Epic 4 / Task 6: TriggerMetadataService + Trigger DTOs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create all trigger DTOs, `TriggerMetadataChangedEvent`, `ITriggerMetadataService` interface, and full `TriggerMetadataService` implementation including trigger history writes and trigger-router link management.

**Architecture:** `TriggerMetadataService` writes a `SyncTriggerHist` row on every create/update (version bumped, `DdlText = null` — DDL install deferred to Epic 5). `DeleteTriggerAsync` explicitly removes `SyncTriggerRouter` rows before deleting the trigger. Cache key format: `"metadata:trigger:{triggerId}"`.

**Tech Stack:** EF Core 9.0.0, MediatR 12.4.1, `Microsoft.Extensions.Caching.Memory` 9.0.0

## Global Constraints

- DbSet names: `db.Triggers`, `db.TriggerHists`, `db.TriggerRouters` (existing names in `AppDbContext`)
- Cache key format: `"metadata:trigger:{triggerId}"`
- Cache expiry: absolute 60 seconds (`AbsoluteExpirationRelativeToNow`)
- All DTOs in `MSOSync.Metadata.Dtos` namespace
- `DdlText = null` in all history rows created this epic (Epic 5 populates it)
- `TriggerVersion` starts at 1 on create, increments by 1 on every update
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Metadata/Dtos/TriggerDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/TriggerHistDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/TriggerRouterDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/CreateTriggerRequest.cs`
- Create: `src/MSOSync.Metadata/Dtos/UpdateTriggerRequest.cs`
- Create: `src/MSOSync.Metadata/Events/TriggerMetadataChangedEvent.cs`
- Create: `src/MSOSync.Metadata/Interfaces/ITriggerMetadataService.cs`
- Create: `src/MSOSync.Metadata/Services/TriggerMetadataService.cs`

**Interfaces:**
- Consumes: `AppDbContext.Triggers`, `AppDbContext.TriggerHists`, `AppDbContext.TriggerRouters`, `DuplicateEntityException`/`NotFoundException` (Task 3), `IMemoryCache`, `IMediator`
- Produces: `ITriggerMetadataService` — consumed by Tasks 8 (registration) and 9 (TriggersController)

---

- [ ] **Step 1: Create DTO types**

```csharp
// src/MSOSync.Metadata/Dtos/TriggerDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record TriggerDto(
    string TriggerId,
    string SourceTable,
    string ChannelId,
    bool SyncOnInsert,
    bool SyncOnUpdate,
    bool SyncOnDelete,
    bool Enabled,
    int TriggerVersion,
    DateTime? LastVerifiedTime);
```

```csharp
// src/MSOSync.Metadata/Dtos/TriggerHistDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record TriggerHistDto(
    long HistId,
    string TriggerId,
    string? DdlText,
    int? TriggerVersion,
    DateTime? CreateTime);
```

```csharp
// src/MSOSync.Metadata/Dtos/TriggerRouterDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record TriggerRouterDto(
    string TriggerId,
    string RouterId,
    bool Enabled);
```

```csharp
// src/MSOSync.Metadata/Dtos/CreateTriggerRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record CreateTriggerRequest(
    string TriggerId,
    string SourceTable,
    string ChannelId,
    bool SyncOnInsert = true,
    bool SyncOnUpdate = true,
    bool SyncOnDelete = true);
```

```csharp
// src/MSOSync.Metadata/Dtos/UpdateTriggerRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record UpdateTriggerRequest(
    string SourceTable,
    string ChannelId,
    bool SyncOnInsert,
    bool SyncOnUpdate,
    bool SyncOnDelete);
```

- [ ] **Step 2: Create `TriggerMetadataChangedEvent`**

```csharp
// src/MSOSync.Metadata/Events/TriggerMetadataChangedEvent.cs
using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record TriggerMetadataChangedEvent(
    string TriggerId,
    string Action) : INotification;
// Action values: "CREATED" | "UPDATED" | "DELETED" | "ENABLED" | "DISABLED" | "ROUTER_ADDED" | "ROUTER_REMOVED"
```

- [ ] **Step 3: Create `ITriggerMetadataService` interface**

```csharp
// src/MSOSync.Metadata/Interfaces/ITriggerMetadataService.cs
using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface ITriggerMetadataService
{
    Task<IReadOnlyList<TriggerDto>> GetTriggersAsync(CancellationToken ct = default);
    Task<TriggerDto?> GetTriggerAsync(string triggerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerDto>> GetTriggersForChannelAsync(string channelId, CancellationToken ct = default);
    Task<TriggerDto> CreateTriggerAsync(CreateTriggerRequest req, CancellationToken ct = default);
    Task<TriggerDto> UpdateTriggerAsync(string triggerId, UpdateTriggerRequest req, CancellationToken ct = default);
    Task DeleteTriggerAsync(string triggerId, CancellationToken ct = default);
    Task EnableTriggerAsync(string triggerId, CancellationToken ct = default);
    Task DisableTriggerAsync(string triggerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerRouterDto>> GetTriggerRoutersAsync(string triggerId, CancellationToken ct = default);
    Task AddTriggerRouterAsync(string triggerId, string routerId, CancellationToken ct = default);
    Task RemoveTriggerRouterAsync(string triggerId, string routerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerHistDto>> GetTriggerHistoryAsync(string triggerId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `TriggerMetadataService` implementation**

```csharp
// src/MSOSync.Metadata/Services/TriggerMetadataService.cs
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
```

- [ ] **Step 5: Build `MSOSync.Metadata` to verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build src\MSOSync.Metadata\MSOSync.Metadata.csproj
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 6: Build full solution**

```powershell
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Metadata/Dtos/TriggerDto.cs
git add src/MSOSync.Metadata/Dtos/TriggerHistDto.cs
git add src/MSOSync.Metadata/Dtos/TriggerRouterDto.cs
git add src/MSOSync.Metadata/Dtos/CreateTriggerRequest.cs
git add src/MSOSync.Metadata/Dtos/UpdateTriggerRequest.cs
git add src/MSOSync.Metadata/Events/TriggerMetadataChangedEvent.cs
git add src/MSOSync.Metadata/Interfaces/ITriggerMetadataService.cs
git add src/MSOSync.Metadata/Services/TriggerMetadataService.cs
git commit -m "feat(metadata): add TriggerMetadataService with history writes and router link management"
```
