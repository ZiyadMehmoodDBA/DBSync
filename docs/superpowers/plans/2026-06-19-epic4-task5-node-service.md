# Epic 4 / Task 5: NodeMetadataService + Node DTOs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Security project reference to `MSOSync.Metadata.csproj`, create all node DTOs, `NodeMetadataChangedEvent`, `INodeMetadataService` interface, and full `NodeMetadataService` implementation including `ApproveRegistrationAsync` single-commit pattern.

**Architecture:** `NodeMetadataService` injects `NodeSecurityService` (from Task 1) directly — no interface needed. `ApproveRegistrationAsync` calls `nodeSecurity.PrepareToken(nodeId)` which stages `SyncNodeSecurity` on the shared `AppDbContext`, then commits registration + node + security in a single `SaveChangesAsync`. Cache key format: `"metadata:node:{nodeId}"`.

**Tech Stack:** EF Core 9.0.0, MediatR 12.4.1, `Microsoft.Extensions.Caching.Memory` 9.0.0

## Global Constraints

- DbSet names: `db.Nodes`, `db.NodeGroups`, `db.NodeSecurities`, `db.RegistrationRequests` (existing names in `AppDbContext`)
- Cache key format: `"metadata:node:{nodeId}"`
- Cache expiry: absolute 60 seconds (`AbsoluteExpirationRelativeToNow`)
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
- Modify: `src/MSOSync.Metadata/MSOSync.Metadata.csproj` — add Security project reference
- Create: `src/MSOSync.Metadata/Dtos/NodeDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/NodeGroupDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/NodeSecurityInfoDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/RegistrationRequestDto.cs`
- Create: `src/MSOSync.Metadata/Dtos/UpdateNodeRequest.cs`
- Create: `src/MSOSync.Metadata/Events/NodeMetadataChangedEvent.cs`
- Create: `src/MSOSync.Metadata/Interfaces/INodeMetadataService.cs`
- Create: `src/MSOSync.Metadata/Services/NodeMetadataService.cs`

**Interfaces:**
- Consumes: `AppDbContext.Nodes`, `AppDbContext.NodeGroups`, `AppDbContext.NodeSecurities`, `AppDbContext.RegistrationRequests`, `NodeSecurityService.PrepareToken(string nodeId) → NodeProvisionResult` (Task 1), `NotFoundException`/`ValidationException` (Task 3), `IMemoryCache`, `IMediator`
- Produces: `INodeMetadataService` — consumed by Tasks 8 (registration) and 9 (NodesController)

---

- [ ] **Step 1: Add Security project reference to `MSOSync.Metadata.csproj`**

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
    <ProjectReference Include="..\MSOSync.Security\MSOSync.Security.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create DTO types**

```csharp
// src/MSOSync.Metadata/Dtos/NodeDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record NodeDto(
    string NodeId,
    string GroupId,
    string SyncUrl,
    string Status,
    DateTime? RegistrationTime,
    DateTime? LastHeartbeat,
    int HeartbeatInterval,
    bool SyncEnabled);
```

```csharp
// src/MSOSync.Metadata/Dtos/NodeGroupDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record NodeGroupDto(string GroupId, string? GroupName);
```

```csharp
// src/MSOSync.Metadata/Dtos/NodeSecurityInfoDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record NodeSecurityInfoDto(
    string NodeId,
    bool HasPendingRotation,
    DateTime? RotationScheduled,
    DateTime? CreatedTime);
```

```csharp
// src/MSOSync.Metadata/Dtos/RegistrationRequestDto.cs
namespace MSOSync.Metadata.Dtos;

public sealed record RegistrationRequestDto(
    long RequestId,
    string NodeId,
    string? NodeGroup,
    string? SyncUrl,
    string? NodeVersion,
    string? DbType,
    DateTime? RequestTime,
    bool Approved);
```

```csharp
// src/MSOSync.Metadata/Dtos/UpdateNodeRequest.cs
namespace MSOSync.Metadata.Dtos;

public sealed record UpdateNodeRequest(
    string GroupId,
    string SyncUrl,
    int HeartbeatInterval);
```

- [ ] **Step 3: Create `NodeMetadataChangedEvent`**

```csharp
// src/MSOSync.Metadata/Events/NodeMetadataChangedEvent.cs
using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record NodeMetadataChangedEvent(
    string NodeId,
    string Action) : INotification;
// Action values: "APPROVED" | "REJECTED" | "ENABLED" | "DISABLED" | "UPDATED"
```

- [ ] **Step 4: Create `INodeMetadataService` interface**

```csharp
// src/MSOSync.Metadata/Interfaces/INodeMetadataService.cs
using MSOSync.Metadata.Dtos;
using MSOSync.Security;

namespace MSOSync.Metadata.Interfaces;

public interface INodeMetadataService
{
    Task<IReadOnlyList<NodeDto>> GetNodesAsync(CancellationToken ct = default);
    Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<NodeGroupDto>> GetNodeGroupsAsync(CancellationToken ct = default);
    Task<NodeDto> UpdateNodeAsync(string nodeId, UpdateNodeRequest req, CancellationToken ct = default);
    Task EnableNodeAsync(string nodeId, CancellationToken ct = default);
    Task DisableNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<RegistrationRequestDto>> GetPendingRegistrationsAsync(CancellationToken ct = default);
    Task<NodeProvisionResult> ApproveRegistrationAsync(long requestId, CancellationToken ct = default);
    Task RejectRegistrationAsync(long requestId, CancellationToken ct = default);
    Task<NodeSecurityInfoDto> GetNodeSecurityInfoAsync(string nodeId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create `NodeMetadataService` implementation**

```csharp
// src/MSOSync.Metadata/Services/NodeMetadataService.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.Services;

public sealed class NodeMetadataService(
    AppDbContext db,
    IMemoryCache cache,
    IMediator mediator,
    NodeSecurityService nodeSecurity) : INodeMetadataService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<NodeDto>> GetNodesAsync(CancellationToken ct = default)
    {
        var nodes = await db.Nodes.AsNoTracking().ToListAsync(ct);
        return nodes.Select(MapNode).ToList().AsReadOnly();
    }

    public async Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:node:{nodeId}";
        if (cache.TryGetValue<NodeDto>(cacheKey, out var cached))
            return cached;

        var node = await db.Nodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node == null) return null;

        var dto = MapNode(node);
        cache.Set(cacheKey, dto, CacheOptions);
        return dto;
    }

    public async Task<IReadOnlyList<NodeGroupDto>> GetNodeGroupsAsync(CancellationToken ct = default)
    {
        var groups = await db.NodeGroups.AsNoTracking().ToListAsync(ct);
        return groups.Select(g => new NodeGroupDto(g.GroupId, g.GroupName)).ToList().AsReadOnly();
    }

    public async Task<NodeDto> UpdateNodeAsync(string nodeId, UpdateNodeRequest req, CancellationToken ct = default)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node '{nodeId}' not found", "NODE_NOT_FOUND");

        node.GroupId = req.GroupId;
        node.SyncUrl = req.SyncUrl;
        node.HeartbeatInterval = req.HeartbeatInterval;

        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:node:{nodeId}");
        await mediator.Publish(new NodeMetadataChangedEvent(nodeId, "UPDATED"), ct);
        return MapNode(node);
    }

    public async Task EnableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node '{nodeId}' not found", "NODE_NOT_FOUND");

        node.SyncEnabled = true;
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:node:{nodeId}");
        await mediator.Publish(new NodeMetadataChangedEvent(nodeId, "ENABLED"), ct);
    }

    public async Task DisableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node '{nodeId}' not found", "NODE_NOT_FOUND");

        node.SyncEnabled = false;
        await db.SaveChangesAsync(ct);
        cache.Remove($"metadata:node:{nodeId}");
        await mediator.Publish(new NodeMetadataChangedEvent(nodeId, "DISABLED"), ct);
    }

    public async Task<IReadOnlyList<RegistrationRequestDto>> GetPendingRegistrationsAsync(CancellationToken ct = default)
    {
        var requests = await db.RegistrationRequests.AsNoTracking()
            .Where(r => !r.Approved)
            .ToListAsync(ct);
        return requests.Select(MapRegistration).ToList().AsReadOnly();
    }

    public async Task<NodeProvisionResult> ApproveRegistrationAsync(long requestId, CancellationToken ct = default)
    {
        var request = await db.RegistrationRequests.FindAsync([requestId], ct)
            ?? throw new NotFoundException($"Registration request {requestId} not found", "REGISTRATION_NOT_FOUND");

        if (request.Approved)
            throw new ValidationException($"Registration request {requestId} is already approved", "ALREADY_APPROVED");

        request.Approved = true;

        db.Nodes.Add(new SyncNode
        {
            NodeId = request.NodeId,
            GroupId = request.NodeGroup ?? "default",
            SyncUrl = request.SyncUrl ?? "http://localhost",
            Status = "APPROVED",
            RegistrationTime = DateTime.UtcNow
        });

        var result = nodeSecurity.PrepareToken(request.NodeId);  // stages SyncNodeSecurity, no SaveChanges

        await db.SaveChangesAsync(ct);  // single commit: request + node + security
        await mediator.Publish(new NodeMetadataChangedEvent(request.NodeId, "APPROVED"), ct);
        return result;
    }

    public async Task RejectRegistrationAsync(long requestId, CancellationToken ct = default)
    {
        var request = await db.RegistrationRequests.FindAsync([requestId], ct)
            ?? throw new NotFoundException($"Registration request {requestId} not found", "REGISTRATION_NOT_FOUND");

        db.RegistrationRequests.Remove(request);
        await db.SaveChangesAsync(ct);
        await mediator.Publish(new NodeMetadataChangedEvent(request.NodeId, "REJECTED"), ct);
    }

    public async Task<NodeSecurityInfoDto> GetNodeSecurityInfoAsync(string nodeId, CancellationToken ct = default)
    {
        var sec = await db.NodeSecurities.AsNoTracking()
            .FirstOrDefaultAsync(s => s.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Security info for node '{nodeId}' not found", "NODE_SECURITY_NOT_FOUND");

        return new NodeSecurityInfoDto(
            sec.NodeId,
            sec.RotationScheduled.HasValue,
            sec.RotationScheduled,
            sec.CreatedTime);
    }

    private static NodeDto MapNode(SyncNode n) =>
        new(n.NodeId, n.GroupId, n.SyncUrl, n.Status,
            n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval, n.SyncEnabled);

    private static RegistrationRequestDto MapRegistration(SyncRegistrationRequest r) =>
        new(r.RequestId, r.NodeId, r.NodeGroup, r.SyncUrl, r.NodeVersion, r.DbType, r.RequestTime, r.Approved);
}
```

- [ ] **Step 6: Build `MSOSync.Metadata` to verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build src\MSOSync.Metadata\MSOSync.Metadata.csproj
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 7: Build full solution**

```powershell
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 8: Commit**

```powershell
git add src/MSOSync.Metadata/MSOSync.Metadata.csproj
git add src/MSOSync.Metadata/Dtos/NodeDto.cs
git add src/MSOSync.Metadata/Dtos/NodeGroupDto.cs
git add src/MSOSync.Metadata/Dtos/NodeSecurityInfoDto.cs
git add src/MSOSync.Metadata/Dtos/RegistrationRequestDto.cs
git add src/MSOSync.Metadata/Dtos/UpdateNodeRequest.cs
git add src/MSOSync.Metadata/Events/NodeMetadataChangedEvent.cs
git add src/MSOSync.Metadata/Interfaces/INodeMetadataService.cs
git add src/MSOSync.Metadata/Services/NodeMetadataService.cs
git commit -m "feat(metadata): add NodeMetadataService with registration approval single-commit pattern"
```
