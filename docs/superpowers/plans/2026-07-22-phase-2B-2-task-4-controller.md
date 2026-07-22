# Task 4 — API Controller

**Files:**
- Create: `src/MSOSync.Api/Dtos/Requests/CreateReplayOperationRequest.cs`
- Create: `src/MSOSync.Api/Validators/CreateReplayOperationRequestValidator.cs`
- Create: `src/MSOSync.Api/Controllers/ReplayController.cs`
- Test: `tests/MSOSync.AppTests/Workers/ReplayWorkerTests.cs` (registry compliance test)

**Interfaces:**
- Consumes from Task 2: `IReplayOperationService`, `IReplayOperationQueryService`, `ReplayOptions`
- Consumes: `INodeAuthorizationService`, `SystemPermissions.ManageNodeLifecycle`
- Produces:
  - `POST /api/v1/operations/replay` → 201
  - `GET /api/v1/operations/replay/{id}` → 200
  - `GET /api/v1/operations/replay/{id}/items` → 200
  - `POST /api/v1/operations/replay/{id}/cancel` → 204

---

- [ ] **Step 1: Create request DTO**

```csharp
// src/MSOSync.Api/Dtos/Requests/CreateReplayOperationRequest.cs
namespace MSOSync.Api.Dtos.Requests;

public sealed record CreateReplayOperationRequest(
    string    NodeId,
    string    ReplayMode,   // "FailedDelivery" | "MissedData" | "Both"
    DateTime  FromTime,
    DateTime  ToTime,
    string[]? ChannelIds,
    long[]?   BatchIds);
```

- [ ] **Step 2: Create validator**

```csharp
// src/MSOSync.Api/Validators/CreateReplayOperationRequestValidator.cs
using FluentValidation;
using MSOSync.Api.Dtos.Requests;

namespace MSOSync.Api.Validators;

public sealed class CreateReplayOperationRequestValidator
    : AbstractValidator<CreateReplayOperationRequest>
{
    private static readonly string[] ValidModes =
        ["FailedDelivery", "MissedData", "Both"];

    public CreateReplayOperationRequestValidator()
    {
        RuleFor(r => r.NodeId).NotEmpty();
        RuleFor(r => r.ReplayMode).Must(m => ValidModes.Contains(m))
            .WithMessage("ReplayMode must be FailedDelivery, MissedData, or Both");
        RuleFor(r => r.FromTime).NotEmpty();
        RuleFor(r => r.ToTime).NotEmpty();
        RuleFor(r => r).Must(r => r.FromTime < r.ToTime)
            .WithMessage("FromTime must be before ToTime");
        RuleFor(r => r).Must(r => (r.ToTime - r.FromTime).TotalDays <= 90)
            .WithMessage("Time range cannot exceed 90 days");
        RuleFor(r => r).Must(r =>
            r.BatchIds is null || r.BatchIds.Length == 0 || r.ReplayMode == "FailedDelivery")
            .WithMessage("BatchIds can only be specified for FailedDelivery mode");
    }
}
```

- [ ] **Step 3: Create `ReplayController`**

```csharp
// src/MSOSync.Api/Controllers/ReplayController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Authorization;
using MSOSync.Api.Dtos.Requests;
using MSOSync.Common.Exceptions;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Operations.Replay;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/operations/replay")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ReplayController(
    IReplayOperationService      replay,
    IReplayOperationQueryService query,
    INodeAuthorizationService    authz) : ControllerBase
{
    private Guid? ActorId => User.Claims
        .Where(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)
        .Select(c => Guid.TryParse(c.Value, out var id) ? (Guid?)id : null)
        .FirstOrDefault();

    [HttpPost]
    [ProducesResponseType(typeof(ReplayOperationCreatedDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create(
        [FromBody] CreateReplayOperationRequest req, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);

        var svcReq = new CreateReplayRequest(
            req.NodeId, req.ReplayMode,
            req.FromTime, req.ToTime,
            req.ChannelIds, req.BatchIds,
            InitiatedBy: ActorId);

        var result = await replay.CreateAsync(svcReq, ct);
        return CreatedAtAction(nameof(GetDetail), new { id = result.OperationId }, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ReplayOperationDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var detail = await query.GetDetailAsync(id, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("{id:guid}/items")]
    [ProducesResponseType(typeof(CursorPageResult<ReplayItemDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetItems(
        Guid id, [FromQuery] string? status,
        [FromQuery] string? cursor, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var filter = new ReplayItemFilter(status, cursor, pageSize);
        var result = await query.GetItemsAsync(id, filter, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await replay.CancelAsync(id, ct);
        return NoContent();
    }
}
```

- [ ] **Step 4: Write AppTest for ReplayWorker registry compliance**

Pattern matches `WorkerRegistryTests` — unit test, no WebApplicationFactory.

```csharp
// tests/MSOSync.AppTests/Workers/ReplayWorkerRegistryTests.cs
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Options;
using MSOSync.Scheduler.Workers;
using Xunit;

namespace MSOSync.AppTests.Workers;

public sealed class ReplayWorkerRegistryTests
{
    [Fact]
    public async Task ReplayWorker_Registers_With_IWorkerStatusRegistry_On_Start()
    {
        var registry = new Mock<IWorkerStatusRegistry>();
        var services = new ServiceCollection().BuildServiceProvider();

        var worker = new ReplayWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReplayOptions { WorkerIntervalSeconds = 10 }),
            registry.Object,
            NullLogger<ReplayWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(cts.Token);

        registry.Verify(r => r.Register(nameof(ReplayWorker), It.IsAny<TimeSpan>()), Times.Once);
    }
}
```

- [ ] **Step 5: Build `MSOSync.Api`**

```
dotnet build src/MSOSync.Api/MSOSync.Api.csproj
```

Expected: 0 errors.

- [ ] **Step 6: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: 0 errors.

- [ ] **Step 7: Run AppTests**

```
dotnet test tests/MSOSync.AppTests --filter "FullyQualifiedName~ReplayWorker" -v normal
```

Expected: PASS (assuming the test infrastructure supports running against a real host).

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Api/Dtos/Requests/CreateReplayOperationRequest.cs
git add src/MSOSync.Api/Validators/CreateReplayOperationRequestValidator.cs
git add src/MSOSync.Api/Controllers/ReplayController.cs
git add tests/MSOSync.AppTests/Workers/ReplayWorkerTests.cs
git commit -m "feat(2B.2-T4): ReplayController + DTOs + validator + AppTest"
```
