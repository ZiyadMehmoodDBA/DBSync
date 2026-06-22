# Task 8: REST API + Program.cs Wiring

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Api/Dtos/Batches/BatchListRequest.cs`
- Create: `src/MSOSync.Api/Dtos/Batches/RetryAllResponse.cs`
- Create: `src/MSOSync.Api/Validators/BatchListRequestValidator.cs`
- Create: `src/MSOSync.Api/Controllers/BatchController.cs`
- Modify: `src/MSOSync.Api/Controllers/TriggersController.cs` (add rebuild + verify endpoints)
- Modify: `src/MSOSync.App/Program.cs` (add 7 DI calls)
- Modify: `src/MSOSync.App/MSOSync.App.csproj` (add project references)

**Interfaces:**
- Consumes: `ITriggerInstallationService`, `ITriggerDriftDetector` (Task 2), `IBatchStateMachine`, `OutgoingBatchDto`, `BatchStatus`, `RetryProcessor` (Task 5), `ICurrentUserService`
- Produces: `POST /api/v1/triggers/{triggerId}/rebuild`, `POST /api/v1/triggers/{triggerId}/verify`, and full `BatchController`

---

- [ ] **Step 1: Create `BatchListRequest`**

```csharp
// src/MSOSync.Api/Dtos/Batches/BatchListRequest.cs
using Microsoft.AspNetCore.Mvc;

namespace MSOSync.Api.Dtos.Batches;

public sealed record BatchListRequest
{
    [FromQuery] public string? Status        { get; init; }
    [FromQuery] public string? NodeId        { get; init; }
    [FromQuery] public string? ChannelId     { get; init; }
    [FromQuery] public int     Page          { get; init; } = 1;
    [FromQuery] public int     PageSize      { get; init; } = 20;
    [FromQuery] public string  SortBy        { get; init; } = "createTime";
    [FromQuery] public string  SortDirection { get; init; } = "desc";
}
```

- [ ] **Step 2: Create `RetryAllResponse`**

```csharp
// src/MSOSync.Api/Dtos/Batches/RetryAllResponse.cs
namespace MSOSync.Api.Dtos.Batches;

public sealed record RetryAllResponse(int Count, DateTime Timestamp, string RequestedBy);
```

- [ ] **Step 3: Create `BatchListRequestValidator`**

```csharp
// src/MSOSync.Api/Validators/BatchListRequestValidator.cs
using FluentValidation;
using MSOSync.Api.Dtos.Batches;

namespace MSOSync.Api.Validators;

public sealed class BatchListRequestValidator : AbstractValidator<BatchListRequest>
{
    private static readonly string[] AllowedSortBy = ["createTime", "batchId", "status"];

    public BatchListRequestValidator()
    {
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("pageSize must be between 1 and 100");

        RuleFor(x => x.SortBy)
            .Must(v => AllowedSortBy.Contains(v))
            .WithMessage($"sortBy must be one of: {string.Join(", ", AllowedSortBy)}");
    }
}
```

- [ ] **Step 4: Create `BatchController`**

```csharp
// src/MSOSync.Api/Controllers/BatchController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Dtos.Batches;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/batches")]
public sealed class BatchController(
    AppDbContext db,
    IBatchStateMachine stateMachine,
    RetryProcessor retryProcessor,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetBatches([FromQuery] BatchListRequest req, CancellationToken ct)
    {
        var query = db.OutgoingBatches.AsNoTracking();

        if (!string.IsNullOrEmpty(req.NodeId))    query = query.Where(b => b.NodeId == req.NodeId);
        if (!string.IsNullOrEmpty(req.ChannelId)) query = query.Where(b => b.ChannelId == req.ChannelId);
        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<BatchStatus>(req.Status, ignoreCase: true, out var status))
            query = query.Where(b => b.Status == (byte)status);

        query = (req.SortBy, req.SortDirection.ToLowerInvariant()) switch
        {
            ("batchId",    "asc")  => query.OrderBy(b => b.BatchId),
            ("batchId",    _)      => query.OrderByDescending(b => b.BatchId),
            ("status",     "asc")  => query.OrderBy(b => b.Status),
            ("status",     _)      => query.OrderByDescending(b => b.Status),
            (_,            "asc")  => query.OrderBy(b => b.CreateTime),
            _                      => query.OrderByDescending(b => b.CreateTime),
        };

        var total      = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(total / (double)req.PageSize);
        var items      = await query
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var data = items.Select(b => new OutgoingBatchDto(
            b.BatchId, (BatchStatus)b.Status, b.NodeId, b.ChannelId,
            b.CreateTime, b.SentTime, b.AckTime, b.RetryCount, b.RowCount, null));

        return Ok(new { data, total, page = req.Page, pageSize = req.PageSize, totalPages });
    }

    [HttpGet("{batchId:long}")]
    [Authorize]
    public async Task<IActionResult> GetBatch(long batchId, CancellationToken ct)
    {
        var batch = await db.OutgoingBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchId == batchId, ct);
        if (batch == null) return NotFound();

        var error = await db.BatchErrors.AsNoTracking()
            .Where(e => e.BatchId == batchId)
            .OrderByDescending(e => e.ErrorId)
            .Select(e => e.ErrorMessage)
            .FirstOrDefaultAsync(ct);

        var dto = new OutgoingBatchDto(
            batch.BatchId, (BatchStatus)batch.Status, batch.NodeId, batch.ChannelId,
            batch.CreateTime, batch.SentTime, batch.AckTime, batch.RetryCount, batch.RowCount, error);

        return Ok(dto);
    }

    [HttpPost("{batchId:long}/retry")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> RetryBatch(long batchId, CancellationToken ct)
    {
        var batch = await db.OutgoingBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchId == batchId, ct);
        if (batch == null) return NotFound();

        var transitioned = await stateMachine.TransitionAsync(
            batchId, BatchStatus.Error, BatchStatus.Retry, ct);

        if (!transitioned)
            return Conflict(new { code = "INVALID_TRANSITION",
                message = $"Batch {batchId} is not in Error status" });

        return Ok();
    }

    [HttpPost("retry-all")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> RetryAll(CancellationToken ct)
    {
        var count = await retryProcessor.ProcessAsync(ct);
        return Ok(new RetryAllResponse(count, DateTime.UtcNow, currentUser.Username));
    }
}
```

- [ ] **Step 5: Add rebuild + verify endpoints to `TriggersController`**

Append to `src/MSOSync.Api/Controllers/TriggersController.cs` — add the two new injected services and two actions. The constructor becomes primary-constructor with two additional parameters:

Replace the existing constructor line:
```csharp
public sealed class TriggersController(ITriggerMetadataService triggerService) : ControllerBase
```

With:
```csharp
public sealed class TriggersController(
    ITriggerMetadataService triggerService,
    ITriggerInstallationService installationService,
    ITriggerDriftDetector driftDetector) : ControllerBase
```

Add the two `using` directives at the top:
```csharp
using MSOSync.Trigger;
```

Append these two actions at the end of the class (before the closing `}`):

```csharp
    [HttpPost("{triggerId}/rebuild")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> RebuildTrigger(string triggerId, CancellationToken ct)
    {
        var result = await installationService.RebuildAsync(triggerId, ct);
        return Ok(result);
    }

    [HttpPost("{triggerId}/verify")]
    [Authorize]
    public async Task<IActionResult> VerifyTrigger(string triggerId, CancellationToken ct)
    {
        var result = await driftDetector.VerifyAsync(triggerId, ct);
        return Ok(result);
    }
```

- [ ] **Step 6: Update `MSOSync.App.csproj`**

Add explicit project references for the new modules (App already references Scheduler which transitively pulls Engine etc., but explicit is clearer):

```xml
<!-- src/MSOSync.App/MSOSync.App.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <Description>ASP.NET Core entry point — wires DI, starts BackgroundService workers</Description>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Enrichers.Thread" />
    <PackageReference Include="Serilog.Enrichers.Environment" />
    <PackageReference Include="FluentValidation.AspNetCore" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Api\MSOSync.Api.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Trigger\MSOSync.Trigger.csproj" />
    <ProjectReference Include="..\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\MSOSync.Routing\MSOSync.Routing.csproj" />
    <ProjectReference Include="..\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\MSOSync.Engine\MSOSync.Engine.csproj" />
    <ProjectReference Include="..\MSOSync.Scheduler\MSOSync.Scheduler.csproj" />
    <ProjectReference Include="..\MSOSync.Metrics\MSOSync.Metrics.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Update `Program.cs`**

Add the 7 new `using` directives and 7 DI registrations after `builder.Services.AddMetadata(builder.Configuration);`:

```csharp
// add at top of file with other usings:
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Routing;
using MSOSync.Scheduler;
using MSOSync.Trigger;
```

After the line `builder.Services.AddMetadata(builder.Configuration);`, insert:

```csharp
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddTriggerEngine(builder.Configuration);
builder.Services.AddEventServices();
builder.Services.AddRoutingServices();
builder.Services.AddBatchPipeline(builder.Configuration);
builder.Services.AddSyncEngine(builder.Configuration);
builder.Services.AddSyncScheduler(builder.Configuration);
```

- [ ] **Step 8: Build full solution**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 9: Commit**

```pwsh
git add src/MSOSync.Api/Dtos/Batches/BatchListRequest.cs `
        src/MSOSync.Api/Dtos/Batches/RetryAllResponse.cs `
        src/MSOSync.Api/Validators/BatchListRequestValidator.cs `
        src/MSOSync.Api/Controllers/BatchController.cs `
        src/MSOSync.Api/Controllers/TriggersController.cs `
        src/MSOSync.App/Program.cs `
        src/MSOSync.App/MSOSync.App.csproj
git commit -m "feat(api,app): add BatchController, trigger rebuild/verify endpoints, wire all Epic 5 modules"
```
