# Epic 4 / Task 4: ParameterMetadataService + ParameterDescriptor Catalog

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `MediatR` and `IMemoryCache` packages to `MSOSync.Metadata.csproj`. Create the `ParameterDescriptor` static catalog, all parameter DTOs, the `IParameterMetadataService` interface, the `ParameterChangedEvent`, and the full `ParameterMetadataService` implementation.

**Architecture:** Static `ParameterDescriptor.Catalog` (code, not DB) holds per-name metadata. Service reads from `db.Parameters` (DbSet name: `Parameters`) and writes history to `db.ParameterHists` (DbSet name: `ParameterHists`). Secret parameters (`IsSecret = true`) masked as `"*****"` in both the DTO and the history row. `UpdateParameterAsync` commits parameter + history in one `SaveChangesAsync`, then invalidates cache, then publishes event.

**Tech Stack:** EF Core 9.0.0, MediatR 12.4.1, `Microsoft.Extensions.Caching.Memory` 9.0.0

## Global Constraints

- DbSet names: `db.Parameters`, `db.ParameterHists` (existing names in `AppDbContext`)
- Cache key format: `"metadata:parameter:{name}"`
- Cache expiry: absolute 60 seconds (`AbsoluteExpirationRelativeToNow`)
- Secret mask constant: `"*****"`
- Unknown parameter name in `UpdateParameterAsync` → `NotFoundException("Parameter '{name}' not found", "PARAMETER_NOT_FOUND")`
- All DTOs in `MSOSync.Metadata.Dtos` namespace
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Modify: `src/MSOSync.Metadata/MSOSync.Metadata.csproj` — add MediatR + MemoryCache packages
- Create: `src/MSOSync.Metadata/Descriptors/ParameterDescriptor.cs`
- Create: `src/MSOSync.Metadata/Dtos/ParameterDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/ParameterHistoryDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/ParameterDescriptorDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/UpdateParameterRequest.cs`
- Create: `src/MSOSync.Metadata/Events/ParameterChangedEvent.cs`
- Create: `src/MSOSync.Metadata/Interfaces/IParameterMetadataService.cs`
- Create: `src/MSOSync.Metadata/Services/ParameterMetadataService.cs`

**Interfaces:**
- Consumes: `AppDbContext.Parameters`, `AppDbContext.ParameterHists`, `ICurrentUserService` (Task 2), `NotFoundException` (Task 3), `IMemoryCache`, `IMediator`
- Produces: `IParameterMetadataService` + all parameter DTOs — consumed by Tasks 8 (registration) and 9 (ParametersController)

---

- [ ] **Step 1: Add `MediatR` and `Microsoft.Extensions.Caching.Memory` to `MSOSync.Metadata.csproj`**

Full file after edit (`src/MSOSync.Metadata/MSOSync.Metadata.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>NodeMetadata, TriggerMetadata, RouterMetadata, ChannelMetadata, ParameterMetadata</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `ParameterDescriptor` static catalog**

```csharp
// src/MSOSync.Metadata/Descriptors/ParameterDescriptor.cs
namespace MSOSync.Metadata.Descriptors;

public sealed record ParameterDescriptor(
    string Name,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic)
{
    public static readonly IReadOnlyDictionary<string, ParameterDescriptor> Catalog =
        new Dictionary<string, ParameterDescriptor>
        {
            ["sync.interval.seconds"]     = new("sync.interval.seconds",     "How often the sync engine runs (seconds)",    false, true,  true),
            ["sync.batch.size"]           = new("sync.batch.size",           "Max events per batch",                        false, false, true),
            ["sync.max.batch.to.send"]    = new("sync.max.batch.to.send",    "Max batches sent per sync cycle",             false, false, true),
            ["sync.retention.days"]       = new("sync.retention.days",       "Purge terminal batches older than N days",    false, false, true),
            ["sync.audit.retention.days"] = new("sync.audit.retention.days", "Purge audit rows older than N days",          false, false, true),
            ["sync.max.retries"]          = new("sync.max.retries",          "Max retry attempts before batch stays ERROR", false, false, true),
        };

    public static ParameterDescriptor Unknown(string name) =>
        new(name, "Unknown parameter", false, false, true);
}
```

- [ ] **Step 3: Create DTO types**

```csharp
// src/MSOSync.Metadata/Dtos/ParameterDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record ParameterDto(
    string Name,
    string? Value,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic);
```

```csharp
// src/MSOSync.Metadata/Dtos/ParameterHistoryDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record ParameterHistoryDto(
    long HistId,
    string ParameterName,
    string? OldValue,
    string? NewValue,
    string? ChangedBy,
    DateTime? ChangeTime);
```

```csharp
// src/MSOSync.Metadata/Dtos/ParameterDescriptorDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record ParameterDescriptorDto(
    string Name,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic);
```

```csharp
// src/MSOSync.Metadata/Dtos/UpdateParameterRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record UpdateParameterRequest(string Value);
```

- [ ] **Step 4: Create `ParameterChangedEvent`**

```csharp
// src/MSOSync.Metadata/Events/ParameterChangedEvent.cs
using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record ParameterChangedEvent(
    string ParameterName,
    string? OldValue,
    string? NewValue) : INotification;
```

- [ ] **Step 5: Create `IParameterMetadataService` interface**

```csharp
// src/MSOSync.Metadata/Interfaces/IParameterMetadataService.cs
using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface IParameterMetadataService
{
    Task<IReadOnlyList<ParameterDto>> GetParametersAsync(CancellationToken ct = default);
    Task<ParameterDto?> GetParameterAsync(string name, CancellationToken ct = default);
    Task UpdateParameterAsync(string name, string value, CancellationToken ct = default);
    Task<IReadOnlyList<ParameterHistoryDto>> GetParameterHistoryAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<ParameterHistoryDto>> GetAllParameterHistoryAsync(CancellationToken ct = default);
}
```

- [ ] **Step 6: Create `ParameterMetadataService` implementation**

```csharp
// src/MSOSync.Metadata/Services/ParameterMetadataService.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Descriptors;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Services;

public sealed class ParameterMetadataService(
    AppDbContext db,
    IMemoryCache cache,
    IMediator mediator,
    ICurrentUserService currentUserService) : IParameterMetadataService
{
    private const string SecretMask = "*****";
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<ParameterDto>> GetParametersAsync(CancellationToken ct = default)
    {
        var parameters = await db.Parameters.AsNoTracking().ToListAsync(ct);
        return parameters.Select(Map).ToList().AsReadOnly();
    }

    public async Task<ParameterDto?> GetParameterAsync(string name, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:parameter:{name}";
        if (cache.TryGetValue<ParameterDto>(cacheKey, out var cached))
            return cached;

        var param = await db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == name, ct);
        if (param == null) return null;

        var dto = Map(param);
        cache.Set(cacheKey, dto, CacheOptions);
        return dto;
    }

    public async Task UpdateParameterAsync(string name, string value, CancellationToken ct = default)
    {
        var param = await db.Parameters.FindAsync([name], ct)
            ?? throw new NotFoundException($"Parameter '{name}' not found", "PARAMETER_NOT_FOUND");

        var descriptor = ParameterDescriptor.Catalog.GetValueOrDefault(name, ParameterDescriptor.Unknown(name));
        var oldValue = descriptor.IsSecret ? SecretMask : param.ParameterValue;
        var newValue = descriptor.IsSecret ? SecretMask : value;

        param.ParameterValue = value;
        db.ParameterHists.Add(new SyncParameterHist
        {
            ParameterName = name,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = currentUserService.GetCurrentUsername(),
            ChangeTime = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        cache.Remove($"metadata:parameter:{name}");
        await mediator.Publish(new ParameterChangedEvent(name, oldValue, newValue), ct);
    }

    public async Task<IReadOnlyList<ParameterHistoryDto>> GetParameterHistoryAsync(
        string name, CancellationToken ct = default)
    {
        var history = await db.ParameterHists.AsNoTracking()
            .Where(h => h.ParameterName == name)
            .OrderByDescending(h => h.ChangeTime)
            .ToListAsync(ct);
        return history.Select(MapHist).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<ParameterHistoryDto>> GetAllParameterHistoryAsync(
        CancellationToken ct = default)
    {
        var history = await db.ParameterHists.AsNoTracking()
            .OrderByDescending(h => h.ChangeTime)
            .ToListAsync(ct);
        return history.Select(MapHist).ToList().AsReadOnly();
    }

    private ParameterDto Map(SyncParameter p)
    {
        var descriptor = ParameterDescriptor.Catalog.GetValueOrDefault(
            p.ParameterName, ParameterDescriptor.Unknown(p.ParameterName));
        var value = descriptor.IsSecret ? SecretMask : p.ParameterValue;
        return new ParameterDto(
            p.ParameterName, value,
            descriptor.Description, descriptor.IsSecret,
            descriptor.RequiresRestart, descriptor.IsDynamic);
    }

    private static ParameterHistoryDto MapHist(SyncParameterHist h) =>
        new(h.HistId, h.ParameterName, h.OldValue, h.NewValue, h.ChangedBy, h.ChangeTime);
}
```

- [ ] **Step 7: Build `MSOSync.Metadata` to verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build src\MSOSync.Metadata\MSOSync.Metadata.csproj
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 8: Build full solution**

```powershell
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 9: Commit**

```powershell
git add src/MSOSync.Metadata/MSOSync.Metadata.csproj
git add src/MSOSync.Metadata/Descriptors/ParameterDescriptor.cs
git add src/MSOSync.Metadata/Dtos/ParameterDto.cs
git add src/MSOSync.Metadata/Dtos/ParameterHistoryDto.cs
git add src/MSOSync.Metadata/Dtos/ParameterDescriptorDto.cs
git add src/MSOSync.Metadata/Dtos/UpdateParameterRequest.cs
git add src/MSOSync.Metadata/Events/ParameterChangedEvent.cs
git add src/MSOSync.Metadata/Interfaces/IParameterMetadataService.cs
git add src/MSOSync.Metadata/Services/ParameterMetadataService.cs
git commit -m "feat(metadata): add ParameterMetadataService with descriptor catalog and history"
```
