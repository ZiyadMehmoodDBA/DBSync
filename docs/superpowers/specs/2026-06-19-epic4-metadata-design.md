# Epic 4: Metadata — Design Specification

**Date:** 2026-06-19
**Status:** Approved
**Epic:** 4 of 12
**Branch:** master

---

## Goal

Implement the metadata management layer for MSOSync CE: CRUD services and REST API controllers for nodes, triggers, routers, channels, and parameters. Remove the plaintext `node_token` column (TD002). Introduce `ICurrentUserService` and the global exception handler. No runtime behavior, no background workers, no sync execution.

---

## Architecture

Interface-based service layer (`I*MetadataService` → `*MetadataService`) registered via `AddMetadata()`. Controllers in `MSOSync.Api` inject service interfaces. All reads are `AsNoTracking`. Cache uses `IMemoryCache` with 60-second absolute expiration. MediatR publishes domain events on writes; no listeners exist yet. DTOs are always returned — EF entities never cross module boundaries. Exception types live in `MSOSync.Common`; a global `IExceptionHandler` in `MSOSync.Api` maps them to the standard error envelope.

**Dependency direction:**
```
MSOSync.Common
    ↑
MSOSync.Persistence
    ↑
MSOSync.Security  ←──── MSOSync.Metadata
    ↑                          ↑
MSOSync.App ─────────── MSOSync.Api
```

`MSOSync.Metadata` depends on `MSOSync.Persistence`, `MSOSync.Security` (for `NodeSecurityService.ProvisionTokenAsync`), and `MSOSync.Common`. It does NOT depend on `MSOSync.Api` or `MSOSync.App`.

---

## Global Constraints

- .NET 9 / C# 13 / ASP.NET Core 9 / EF Core 9.0.0 / SQL Server
- All routes: `api/v1/`
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- No EF InMemory provider in tests — use SQLite in-memory for unit tests
- Integration tests use Testcontainers SQL Server (package already in `Directory.Packages.props`)
- DTOs always returned from services and controllers — no raw EF entities
- Cache: `IMemoryCache`, absolute 60s expiration, key format `metadata:{domain}:{id}`
- Exception types in `MSOSync.Common/Exceptions/`
- Secrets never logged or returned in API responses
- Parameter history: secret values masked as `*****` in DB and DTO

---

## Task Sequence

1. M011 migration + entity cleanup + `NodeSecurityService.ProvisionTokenAsync`
2. `ICurrentUserService` in Common + `HttpContextCurrentUserService` in App
3. Exception hierarchy in Common + `GlobalExceptionHandler` in Api
4. `IParameterMetadataService` + `ParameterMetadataService` + `ParameterDescriptor` catalog
5. `INodeMetadataService` + `NodeMetadataService`
6. `ITriggerMetadataService` + `TriggerMetadataService`
7. `IRouterMetadataService` + `RouterMetadataService` + `IChannelMetadataService` + `ChannelMetadataService`
8. `AddMetadata()` extension + wire into `Program.cs`
9. API controllers: `ParametersController`, `NodesController`, `TriggersController`, `RoutersController`, `ChannelsController`, `MetadataController` + DTOs + FluentValidation validators
10. Unit tests (`MSOSync.MetadataTests`)
11. Integration tests (`MSOSync.IntegrationTests/Metadata/`)

---

## File Map

### New / Modified

```
src/MSOSync.Common/
├── Exceptions/
│   ├── SyncException.cs
│   ├── NotFoundException.cs
│   ├── DuplicateEntityException.cs
│   ├── ValidationException.cs
│   ├── ForbiddenOperationException.cs
│   ├── ConcurrencyException.cs
│   └── UnauthorizedException.cs
└── ICurrentUserService.cs

src/MSOSync.App/
└── HttpContextCurrentUserService.cs         (modify Program.cs: register ICurrentUserService)

src/MSOSync.Persistence/
└── Entities/
    └── SyncNodeSecurity.cs                  (remove NodeToken property)

src/MSOSync.Persistence/Migrations/
├── M011_RemovePlaintextNodeToken.cs
└── M011_RemovePlaintextNodeToken.Designer.cs

src/MSOSync.Security/
└── NodeSecurityService.cs                   (add ProvisionTokenAsync)
    + INodeSecurityService.cs (if interface doesn't exist, extract it)

src/MSOSync.Metadata/
├── MetadataServiceExtensions.cs
├── Events/
│   ├── NodeMetadataChangedEvent.cs
│   ├── TriggerMetadataChangedEvent.cs
│   ├── RouterMetadataChangedEvent.cs
│   ├── ChannelMetadataChangedEvent.cs
│   └── ParameterChangedEvent.cs
├── Descriptors/
│   └── ParameterDescriptor.cs               (static catalog)
├── Interfaces/
│   ├── INodeMetadataService.cs
│   ├── ITriggerMetadataService.cs
│   ├── IRouterMetadataService.cs
│   ├── IChannelMetadataService.cs
│   └── IParameterMetadataService.cs
├── Services/
│   ├── NodeMetadataService.cs
│   ├── TriggerMetadataService.cs
│   ├── RouterMetadataService.cs
│   ├── ChannelMetadataService.cs
│   └── ParameterMetadataService.cs
└── Dtos/                                    (all types used in service interfaces live here)
    ├── NodeDto.cs
    ├── NodeGroupDto.cs
    ├── NodeSecurityInfoDto.cs
    ├── RegistrationRequestDto.cs
    ├── UpdateNodeRequest.cs
    ├── TriggerDto.cs
    ├── TriggerRouterDto.cs
    ├── TriggerHistDto.cs
    ├── CreateTriggerRequest.cs
    ├── UpdateTriggerRequest.cs
    ├── RouterDto.cs
    ├── CreateRouterRequest.cs
    ├── UpdateRouterRequest.cs
    ├── ChannelDto.cs
    ├── CreateChannelRequest.cs
    ├── UpdateChannelRequest.cs
    ├── ParameterDto.cs
    ├── ParameterHistoryDto.cs
    ├── ParameterDescriptorDto.cs
    └── UpdateParameterRequest.cs

src/MSOSync.Api/
├── Controllers/
│   ├── NodesController.cs
│   ├── TriggersController.cs
│   ├── RoutersController.cs
│   ├── ChannelsController.cs
│   ├── ParametersController.cs
│   └── MetadataController.cs
├── Dtos/
│   └── Nodes/
│       └── AddTriggerRouterRequest.cs       (HTTP body only: { routerId }; not used in service interface)
├── Validators/
│   ├── CreateTriggerRequestValidator.cs
│   ├── CreateRouterRequestValidator.cs
│   ├── CreateChannelRequestValidator.cs
│   └── UpdateParameterRequestValidator.cs
└── Exceptions/
    └── GlobalExceptionHandler.cs

tests/MSOSync.MetadataTests/
├── MSOSync.MetadataTests.csproj
├── ParameterMetadataServiceTests.cs
├── NodeMetadataServiceTests.cs
├── TriggerMetadataServiceTests.cs
├── RouterMetadataServiceTests.cs
└── ChannelMetadataServiceTests.cs

tests/MSOSync.IntegrationTests/
└── Metadata/
    ├── MetadataFixture.cs
    └── MetadataTests.cs
```

---

## Task 1 — M011 Migration + Entity Cleanup + ProvisionTokenAsync

### Migration

**File:** `src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.cs`

```csharp
[Migration("20260619000011_RemovePlaintextNodeToken")]
public partial class M011_RemovePlaintextNodeToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // IF EXISTS guard — resilient against partially migrated DBs
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('msosync.sync_node_security')
                  AND name = 'node_token'
            )
            BEGIN
                ALTER TABLE msosync.sync_node_security DROP COLUMN node_token
            END
            """);

        migrationBuilder.AddColumn<DateTime>(
            name: "rotation_scheduled",
            schema: "msosync",
            table: "sync_node_security",
            type: "datetime2",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "rotation_scheduled",
            schema: "msosync",
            table: "sync_node_security");

        migrationBuilder.AddColumn<string>(
            name: "node_token",
            schema: "msosync",
            table: "sync_node_security",
            type: "varchar(255)",
            unicode: false,
            maxLength: 255,
            nullable: true);
    }
}
```

Designer.cs: `[DbContext(typeof(AppDbContext))]` only. No `[Migration]` attribute.

### Entity Update

**File:** `src/MSOSync.Persistence/Entities/SyncNodeSecurity.cs`

Remove `NodeToken` property. Final entity:

```csharp
public sealed class SyncNodeSecurity
{
    public string NodeId { get; set; } = null!;
    public string CurrentTokenHash { get; set; } = null!;
    public string? NextTokenHash { get; set; }
    public DateTime? RotationScheduled { get; set; }
    public DateTime? CreatedTime { get; set; }
}
```

### NodeSecurityService.PrepareToken

Add to `NodeSecurityService` (and its interface if one exists):

```csharp
public NodeProvisionResult PrepareToken(string nodeId)
{
    var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    var hash = hasher.Hash(raw);  // BCryptPasswordHasher

    var existing = db.NodeSecurities.Local.FirstOrDefault(s => s.NodeId == nodeId)
        ?? db.NodeSecurities.Find(nodeId);

    if (existing != null)
    {
        existing.CurrentTokenHash = hash;
        existing.NextTokenHash = null;
        existing.RotationScheduled = null;
    }
    else
    {
        db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = nodeId,
            CurrentTokenHash = hash,
            CreatedTime = DateTime.UtcNow
        });
    }

    return new NodeProvisionResult(nodeId, raw);
}
```

**Transaction-neutral:** `PrepareToken` stages changes on the shared `AppDbContext` but does NOT call `SaveChangesAsync`. The caller (`NodeMetadataService.ApproveRegistrationAsync`) owns the single `SaveChangesAsync` that commits the registration, the new node, and the token in one atomic transaction.

`NodeProvisionResult` record in `MSOSync.Security`:

```csharp
public sealed record NodeProvisionResult(string NodeId, string RawToken);
```

### Tests to verify

- Build solution: `dotnet build MSOSync.sln` — 0 warnings, 0 errors
- Integration: confirm `node_token` absent from `sys.columns`, `rotation_scheduled` present
- `PrepareToken` stages SyncNodeSecurity row without calling SaveChanges
- `PrepareToken` generates a verifiable BCrypt hash

---

## Task 2 — ICurrentUserService

**File:** `src/MSOSync.Common/ICurrentUserService.cs`

```csharp
namespace MSOSync.Common;

public interface ICurrentUserService
{
    string GetCurrentUsername();
}
```

**File:** `src/MSOSync.App/HttpContextCurrentUserService.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MSOSync.Common;

namespace MSOSync.App;

public sealed class HttpContextCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string GetCurrentUsername() =>
        accessor.HttpContext?.User?.Identity?.Name ?? "system";
}
```

**`Program.cs` additions:**

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
```

**JwtService update:** Add `ClaimTypes.Name` claim to `CreateAccessToken`:

```csharp
new Claim(ClaimTypes.Name, username),
```

Alongside the existing `JwtRegisteredClaimNames.Sub` claim (keep both for compatibility).

### Tests to verify

- `GetCurrentUsername()` returns `Identity.Name` when HttpContext present
- Returns `"system"` when `HttpContext` is null (background job scenario)

---

## Task 3 — Exception Hierarchy + GlobalExceptionHandler

### Exception types

**File:** `src/MSOSync.Common/Exceptions/SyncException.cs`

```csharp
namespace MSOSync.Common.Exceptions;

public abstract class SyncException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
```

Concrete types in `MSOSync.Common/Exceptions/`:

```csharp
public sealed class NotFoundException(string message, string code = "NOT_FOUND")
    : SyncException(message, code);

public sealed class DuplicateEntityException(string message, string code = "DUPLICATE_ENTITY")
    : SyncException(message, code);

public sealed class ValidationException(string message, string code = "VALIDATION_ERROR")
    : SyncException(message, code);

public sealed class ForbiddenOperationException(string message, string code = "FORBIDDEN")
    : SyncException(message, code);

public sealed class ConcurrencyException(string message, string code = "CONCURRENCY_CONFLICT")
    : SyncException(message, code);

public sealed class UnauthorizedException(string message, string code = "UNAUTHORIZED")
    : SyncException(message, code);
```

### GlobalExceptionHandler

**File:** `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Exceptions;

namespace MSOSync.Api.Exceptions;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken ct)
    {
        var (status, error, code, message) = exception switch
        {
            NotFoundException ex           => (404, "Not Found",             ex.Code, ex.Message),
            DuplicateEntityException ex    => (409, "Conflict",              ex.Code, ex.Message),
            ValidationException ex         => (400, "Bad Request",           ex.Code, ex.Message),
            ForbiddenOperationException ex => (403, "Forbidden",             ex.Code, ex.Message),
            ConcurrencyException ex        => (409, "Conflict",              ex.Code, ex.Message),
            UnauthorizedException ex       => (401, "Unauthorized",          ex.Code, ex.Message),
            _                              => (500, "Internal Server Error", "INTERNAL_SERVER_ERROR", "An unexpected error occurred")
        };

        if (status == 500)
            logger.LogError(exception, "Unhandled exception");

        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            timestamp = DateTime.UtcNow,
            status,
            error,
            code,
            message,
            correlationId
        }, ct);

        return true;
    }
}
```

**`Program.cs` additions:**

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// ...
app.UseExceptionHandler();  // before UseAuthentication
```

### Tests to verify

- `NotFoundException` → 404 with correct code
- `DuplicateEntityException` → 409
- `ValidationException` → 400
- `ForbiddenOperationException` → 403
- `ConcurrencyException` → 409
- Unhandled `Exception` → 500, body contains only `"INTERNAL_SERVER_ERROR"` (no stack trace)

---

## Task 4 — ParameterMetadataService

### ParameterDescriptor (static catalog)

**File:** `src/MSOSync.Metadata/Descriptors/ParameterDescriptor.cs`

```csharp
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
            ["sync.interval.seconds"]    = new("sync.interval.seconds",    "How often the sync engine runs (seconds)",    false, true,  true),
            ["sync.batch.size"]          = new("sync.batch.size",          "Max events per batch",                        false, false, true),
            ["sync.max.batch.to.send"]   = new("sync.max.batch.to.send",   "Max batches sent per sync cycle",             false, false, true),
            ["sync.retention.days"]      = new("sync.retention.days",      "Purge terminal batches older than N days",    false, false, true),
            ["sync.audit.retention.days"]= new("sync.audit.retention.days","Purge audit rows older than N days",          false, false, true),
            ["sync.max.retries"]         = new("sync.max.retries",         "Max retry attempts before batch stays ERROR", false, false, true),
        };

    public static ParameterDescriptor Unknown(string name) =>
        new(name, "Unknown parameter", false, false, true);
}
```

### Interface

**File:** `src/MSOSync.Metadata/Interfaces/IParameterMetadataService.cs`

```csharp
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

### DTOs (internal to Metadata)

```csharp
// MSOSync.Metadata/Dtos/ParameterDto.cs
public sealed record ParameterDto(
    string Name,
    string? Value,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic);

// MSOSync.Metadata/Dtos/ParameterHistoryDto.cs
public sealed record ParameterHistoryDto(
    long HistId,
    string ParameterName,
    string? OldValue,
    string? NewValue,
    string? ChangedBy,
    DateTime? ChangeTime);

// MSOSync.Metadata/Dtos/ParameterDescriptorDto.cs
public sealed record ParameterDescriptorDto(
    string Name,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic);
```

`GET /parameters/descriptors` controller maps `ParameterDescriptor.Catalog.Values` → `ParameterDescriptorDto` directly (no service call needed — catalog is static).

### Implementation

**File:** `src/MSOSync.Metadata/Services/ParameterMetadataService.cs`

Key behaviors:
1. `GetParameterAsync` — cache hit returns from `IMemoryCache` (`metadata:parameter:{name}`). On miss: query DB, merge with `ParameterDescriptor.Catalog`, mask value if `IsSecret`, set in cache, return.
2. `UpdateParameterAsync` — read old value from DB; update `sync_parameter.parameter_value`; insert `sync_parameter_hist` row with `OldValue`/`NewValue` masked if secret, `ChangedBy = currentUserService.GetCurrentUsername()`, `ChangeTime = UtcNow` — both in one `SaveChangesAsync` call; invalidate cache; publish `ParameterChangedEvent`.
3. Secret masking constant: `"*****"`.

```csharp
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

    public async Task UpdateParameterAsync(string name, string value, CancellationToken ct = default)
    {
        var param = await db.Parameters.FindAsync([name], ct)
            ?? throw new NotFoundException($"Parameter '{name}' not found", "PARAMETER_NOT_FOUND");

        var descriptor = ParameterDescriptor.Catalog.GetValueOrDefault(name, ParameterDescriptor.Unknown(name));
        var oldValue = descriptor.IsSecret ? SecretMask : param.ParameterValue;
        var newValue = descriptor.IsSecret ? SecretMask : value;

        param.ParameterValue = value;
        db.ParameterHistories.Add(new SyncParameterHist
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
    // ... GetParameterAsync, GetParametersAsync, GetParameterHistoryAsync, GetAllParameterHistoryAsync
}
```

`AppDbContext` must expose `ParameterHistories` DbSet. If missing, add in this task.

### Tests to verify

- `UpdateParameterAsync` writes history row in same transaction
- Secret parameter: history row contains `"*****"` not real value
- `UpdateParameterAsync` publishes `ParameterChangedEvent`
- Cache invalidated after update: second `GetParameterAsync` returns updated value
- Unknown parameter name → `NotFoundException`
- `GetAllParameterHistoryAsync` returns all history rows across all parameters

---

## Task 5 — NodeMetadataService

### Interface

```csharp
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

### ApproveRegistrationAsync flow

```
1. Load SyncRegistrationRequest by requestId
   → NotFoundException if not found
   → ValidationException if already approved

2. Set request.Approved = true
3. Create SyncNode (status = "APPROVED", group from request, sync_url from request)
4. Call NodeSecurityService.PrepareToken(nodeId) → NodeProvisionResult
   (stages SyncNodeSecurity on the shared DbContext — does NOT SaveChanges)
5. SaveChangesAsync — single commit: registration update + new node + new security row
6. Publish NodeMetadataChangedEvent
7. Return NodeProvisionResult (raw token shown once)
```

### RejectRegistrationAsync flow

```
1. Load SyncRegistrationRequest
   → NotFoundException if not found
2. Delete request row (or set a rejected flag — delete is simpler)
3. SaveChangesAsync
4. Publish NodeMetadataChangedEvent
```

### NodeSecurityInfoDto

```csharp
public sealed record NodeSecurityInfoDto(
    string NodeId,
    bool HasPendingRotation,
    DateTime? RotationScheduled,
    DateTime? CreatedTime);
// Never includes CurrentTokenHash or NextTokenHash
```

### Tests to verify

- `ApproveRegistrationAsync` creates `SyncNode` + `SyncNodeSecurity`, returns raw token
- Token verifies: `BCrypt.Verify(result.RawToken, db.NodeSecurities.Find(nodeId).CurrentTokenHash)`
- `RejectRegistrationAsync` removes registration request
- `EnableNodeAsync` / `DisableNodeAsync` toggle `SyncNode.SyncEnabled`
- `GetNodeSecurityInfoAsync` never returns hash values
- Approving non-existent request → `NotFoundException`
- `NodeMetadataChangedEvent` published on approve/reject/enable/disable

---

## Task 6 — TriggerMetadataService

### Interface

```csharp
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

### TriggerHist writes

On `CreateTriggerAsync` and `UpdateTriggerAsync`, bump `TriggerVersion` and write to `sync_trigger_hist`:

```csharp
db.TriggerHistories.Add(new SyncTriggerHist
{
    TriggerId = trigger.TriggerId,
    DdlText = null,          // DDL is Epic 5; record metadata change only
    TriggerVersion = trigger.TriggerVersion,
    CreateTime = DateTime.UtcNow
});
```

`DdlText` is null in Epic 4 — Epic 5 will populate this when it installs the SQL Server trigger.

### Duplicate check

`CreateTriggerAsync`: if `sync_trigger` already has a row with the same `trigger_id` → `DuplicateEntityException("Trigger '{id}' already exists", "DUPLICATE_TRIGGER")`.

### Tests to verify

- Create trigger → TriggerHist row written with bumped version
- `GetTriggersForChannelAsync` returns only triggers for the given channel
- Adding/removing trigger-router links
- Duplicate trigger → `DuplicateEntityException`
- Delete → also cleans up `sync_trigger_router` rows (cascade or explicit delete)
- Enable/disable toggles `Enabled`
- Cache invalidated after update
- `TriggerMetadataChangedEvent` published

---

## Task 7 — RouterMetadataService + ChannelMetadataService

### IRouterMetadataService

```csharp
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

### IChannelMetadataService

```csharp
public interface IChannelMetadataService
{
    Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(CancellationToken ct = default);
    Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken ct = default);
    Task<ChannelDto> CreateChannelAsync(CreateChannelRequest req, CancellationToken ct = default);
    Task<ChannelDto> UpdateChannelAsync(string channelId, UpdateChannelRequest req, CancellationToken ct = default);
    Task DeleteChannelAsync(string channelId, CancellationToken ct = default);
}
```

### Validation rules

**Channel:**
- `BatchSize`: 1–10000
- `MaxBatchToSend`: 1–100
- `MaxDataSize`: 1024–104857600 (1KB–100MB)
- `Priority`: 1–1000

**Router:**
- `RouterType`: must be one of `default`, `column`, `subselect`
- `SourceNodeGroup` and `TargetNodeGroup` must reference existing groups

### Tests to verify

- `GetRoutersForSourceGroupAsync` / `GetRoutersForTargetGroupAsync` filter correctly
- Duplicate router → `DuplicateEntityException`
- Channel validation: batch size out of range → `ValidationException`
- Events published on all writes

---

## Task 8 — AddMetadata() + Program.cs

**File:** `src/MSOSync.Metadata/MetadataServiceExtensions.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Services;

namespace MSOSync.Metadata;

public static class MetadataServiceExtensions
{
    public static IServiceCollection AddMetadata(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMemoryCache();
        services.AddScoped<INodeMetadataService, NodeMetadataService>();
        services.AddScoped<ITriggerMetadataService, TriggerMetadataService>();
        services.AddScoped<IRouterMetadataService, RouterMetadataService>();
        services.AddScoped<IChannelMetadataService, ChannelMetadataService>();
        services.AddScoped<IParameterMetadataService, ParameterMetadataService>();
        return services;
    }
}
```

**`Program.cs` additions** (after `AddSecurity`, before `AddControllers`):

```csharp
builder.Services.AddMetadata(builder.Configuration);
```

MediatR is already registered from `AddSecurity()`. Metadata events are in `MSOSync.Metadata` — extend MediatR scan:

```csharp
// In AddSecurity() or AddMetadata():
services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssemblyContaining<AuditService>();
    cfg.RegisterServicesFromAssemblyContaining<ParameterMetadataService>();
});
```

Or pass both assemblies in one call.

---

## Task 9 — API Controllers + DTOs + Validators

### Controller pattern (identical to AuthController)

```csharp
[ApiController]
[Route("api/v1/nodes")]
public sealed class NodesController(INodeMetadataService nodeService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
    {
        var nodes = await nodeService.GetNodesAsync(ct);
        return Ok(nodes);
    }
    // ...
}
```

### Authorization

| Method | Policy |
|--------|--------|
| All GET | `[Authorize]` |
| POST, PUT (update), enable/disable, approve, reject | `[Authorize(Policy = "OperatorOrAbove")]` |
| DELETE | `[Authorize(Policy = "AdminOnly")]` |
| PUT /parameters/{name} | `[Authorize(Policy = "AdminOnly")]` |
| GET /nodes/{nodeId}/security | `[Authorize(Policy = "AdminOnly")]` |

### MetadataController

```csharp
[ApiController]
[Route("api/v1/metadata")]
public sealed class MetadataController(
    INodeMetadataService nodes,
    ITriggerMetadataService triggers,
    IRouterMetadataService routers,
    IChannelMetadataService channels,
    IParameterMetadataService parameters) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var allNodes      = await nodes.GetNodesAsync(ct);
        var allTriggers   = await triggers.GetTriggersAsync(ct);
        var allRouters    = await routers.GetRoutersAsync(ct);
        var allChannels   = await channels.GetChannelsAsync(ct);
        var allParameters = await parameters.GetParametersAsync(ct);

        return Ok(new
        {
            nodes      = allNodes.Count,
            triggers   = allTriggers.Count,
            routers    = allRouters.Count,
            channels   = allChannels.Count,
            parameters = allParameters.Count
        });
    }
}
```

### FluentValidation validators

Validators for all create/update request types. Key rules:

```csharp
// CreateTriggerRequestValidator
RuleFor(x => x.TriggerId).NotEmpty().MaximumLength(50).Matches(@"^[a-z0-9_\-]+$");
RuleFor(x => x.SourceTable).NotEmpty().MaximumLength(128);
RuleFor(x => x.ChannelId).NotEmpty().MaximumLength(50);

// CreateChannelRequestValidator
RuleFor(x => x.ChannelId).NotEmpty().MaximumLength(50);
RuleFor(x => x.BatchSize).InclusiveBetween(1, 10000);
RuleFor(x => x.MaxBatchToSend).InclusiveBetween(1, 100);
RuleFor(x => x.Priority).InclusiveBetween(1, 1000);

// UpdateParameterRequestValidator
RuleFor(x => x.Value).NotNull().MaximumLength(4000);
```

### Register validators

Add to `Program.cs` (already uses `AddValidatorsFromAssemblyContaining<AuthController>()`):

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateTriggerRequestValidator>();
```

Or if all validators are in `MSOSync.Api`, the existing scan already covers them.

### Response codes

| Situation | Code |
|-----------|------|
| Successful GET list or single | 200 |
| Successful POST (create) | 201 + `CreatedAtAction` |
| Successful PUT (update) / enable / disable / approve | 200 |
| Successful DELETE / reject | 204 |
| Not found | 404 (via `NotFoundException` → handler) |
| Duplicate entity | 409 (via `DuplicateEntityException` → handler) |
| Validation failure | 400 (FluentValidation auto-validation or `ValidationException`) |
| Forbidden | 403 |
| Server error | 500 |

---

## Task 10 — Unit Tests (MSOSync.MetadataTests)

**New project:** `tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Security\MSOSync.Security.csproj" />
  </ItemGroup>
</Project>
```

Add to `Directory.Packages.props` Testing group (if not already present):
```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
<PackageVersion Include="Moq" Version="4.20.72" />
```

Check existing entries first — `Moq` may already be present from `MSOSync.SecurityTests`.

### SQLite setup helper

```csharp
internal static class TestDbContext
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
```

### Coverage targets

| Area | Target |
|------|--------|
| Metadata services | 95% |
| Exception handler | 100% |
| DTO validators | 90% |

### ParameterMetadataServiceTests — required cases

```
UpdateParameterAsync_KnownParameter_WritesHistoryRow
UpdateParameterAsync_SecretParameter_MasksValueInHistory
UpdateParameterAsync_PublishesParameterChangedEvent
UpdateParameterAsync_AfterUpdate_CacheReturnsNewValue  ← cache invalidation test
UpdateParameterAsync_UnknownName_ThrowsNotFoundException
GetParameterAsync_SecretParameter_MasksValueInDto
GetAllParameterHistoryAsync_ReturnsAllRows
```

### NodeMetadataServiceTests — required cases

```
ApproveRegistrationAsync_ValidRequest_CreatesNodeAndReturnsToken
ApproveRegistrationAsync_TokenVerifies_BCryptHashMatch
RejectRegistrationAsync_RemovesRegistrationRequest
EnableNodeAsync_SetsEnabledTrue
DisableNodeAsync_SetsEnabledFalse
GetNodeSecurityInfoAsync_NeverReturnsHashValues
ApproveRegistrationAsync_NonExistentRequest_ThrowsNotFoundException
ApproveRegistrationAsync_PublishesNodeMetadataChangedEvent
```

### TriggerMetadataServiceTests — required cases

```
CreateTriggerAsync_WritesHistoryRow
CreateTriggerAsync_DuplicateId_ThrowsDuplicateEntityException
UpdateTriggerAsync_BumpsTriggerVersion
UpdateTriggerAsync_InvalidatesCache
GetTriggersForChannelAsync_ReturnsOnlyMatchingChannel
EnableTriggerAsync_SetsEnabledTrue
DisableTriggerAsync_SetsEnabledFalse
AddTriggerRouterAsync_CreatesTriggerRouterRow
RemoveTriggerRouterAsync_DeletesTriggerRouterRow
```

### Exception handler tests (in `MSOSync.MetadataTests` or `MSOSync.IntegrationTests`)

```
NotFoundException_Returns404WithCode
DuplicateEntityException_Returns409WithCode
ValidationException_Returns400WithCode
ForbiddenOperationException_Returns403WithCode
UnhandledException_Returns500WithGenericCode_NoStackTrace
```

---

## Task 11 — Integration Tests

**New folder:** `tests/MSOSync.IntegrationTests/Metadata/`

### MetadataFixture

Extends `WebApplicationFactory<Program>` + `IAsyncLifetime`. Uses Testcontainers SQL Server:

```csharp
using Testcontainers.MsSql;

public sealed class MetadataFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Same CreateHost pattern as SecurityFixture — override WebApplication directly
        // Override ConnectionStrings:DefaultConnection with container.GetConnectionString()
        // ...
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // migrate + seed test data
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Metadata")]
public sealed class MetadataCollection : ICollectionFixture<MetadataFixture> { }
```

### MetadataTests — required integration tests

```
TriggerCrud_CreateGetUpdateDelete_RoundTrip
ParameterUpdate_WritesHistoryRow_InDatabase
ParameterUpdate_SecretParameter_HistoryRowMasked
RegistrationApproval_RawTokenVerifiesAgainstStoredHash
SecurityEndpoint_NeverReturnsHashValues
DuplicateTrigger_Returns409Conflict
ExceptionHandler_NotFound_Returns404WithEnvelope
ExceptionHandler_UnexpectedError_Returns500WithoutStackTrace
ChannelCrud_CreateGetUpdateDelete_RoundTrip
RouterSourceGroup_FiltersCorrectly
```

---

## MediatR Events

**File:** `src/MSOSync.Metadata/Events/`

```csharp
public sealed record ParameterChangedEvent(
    string ParameterName, string? OldValue, string? NewValue) : INotification;

public sealed record NodeMetadataChangedEvent(
    string NodeId, string Action) : INotification;   // Action: "APPROVED"|"REJECTED"|"ENABLED"|"DISABLED"

public sealed record TriggerMetadataChangedEvent(
    string TriggerId, string Action) : INotification;

public sealed record RouterMetadataChangedEvent(
    string RouterId, string Action) : INotification;

public sealed record ChannelMetadataChangedEvent(
    string ChannelId, string Action) : INotification;
```

No handlers registered in Epic 4. Future epics subscribe as needed.

---

## Deferred to Later Epics

| Item | Target |
|------|--------|
| Trigger rebuild / verify / drift detection | Epic 5 |
| Routing resolution (trigger → target nodes) | Epic 7 |
| BatchProperties / TransportProperties typed config | Epic 8 / 9 |
| FeatureFlagService, SyncEdition | Epic 11+ |
| NodeStartupConfig, HeartbeatService | Epic 10 |
| Force sync, node status transitions | Epic 10 |
| Pagination on metadata lists | When needed |

---

## Technical Debt

| ID | Item |
|----|------|
| TD001 | O(N) BCrypt scan in RefreshAsync — Epic 6+ |
| TD002 | ✅ Resolved in M011 (this epic) |
| TD003 | JWT iss/aud validation — Epic 8 |
| TD004 | IP rate limiting — Epic 8 |
