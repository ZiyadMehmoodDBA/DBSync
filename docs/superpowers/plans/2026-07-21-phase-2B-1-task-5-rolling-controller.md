# 2B.1 Task 5 — RollingOperationsController

**Files:**
- Create: `src/MSOSync.Api/Dtos/Operations/RollingOperationDtos.cs`
- Create: `src/MSOSync.Api/Validators/CreateRollingOperationRequestValidator.cs`
- Create: `src/MSOSync.Metadata/Operations/Rolling/IRollingOperationQueryService.cs` + `RollingOperationQueryService.cs`
- Create: `src/MSOSync.Api/Controllers/RollingOperationsController.cs`
- Modify: Metadata DI extension (register query service)
- Test: Create `tests/MSOSync.MetadataTests/Operations/RollingOperationQueryServiceTests.cs`

**Interfaces:**
- Consumes: Task 4 (`IRollingOperationService` exact signature, `RollingOperationPolicy`, `RollingStepStatus`, `OperationStateException` → 409 mapping), existing `INodeAuthorizationService.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct)` pattern, `OperationType`.
- Produces:

```csharp
public sealed record CreateRollingOperationRequest(
    string Kind,                       // "RollingMaintenance" | "RollingUpgrade"
    IReadOnlyList<string> NodeIds,
    int?    WaveSize,
    int?    WavePercent,
    int     GateSoakSeconds,
    string  WaveAction,                // "manual-confirm" | "auto-window"
    int?    WindowSeconds,
    string? TargetVersion,
    int     VerificationTimeoutSeconds);

public sealed record CreateRollingOperationResponse(Guid OperationId);
public sealed record RollingStepDto(Guid StepId, string NodeId, int WaveNumber, string Status,
    DateTime? StartedAt, DateTime? CompletedAt, string? ErrorMessage);
public sealed record RollingOperationDetailDto(Guid OperationId, string OperationType, string Status,
    string? Result, RollingOperationPolicy Policy, IReadOnlyList<RollingStepDto> Steps);
```

- `IRollingOperationQueryService.GetDetailAsync(Guid operationId, CancellationToken ct)` → `RollingOperationDetailDto` (throws `NotFoundException` code `OPERATION_NOT_FOUND`).
- Routes: `POST /api/v1/operations/rolling`, `GET /api/v1/operations/rolling/{id}`, `POST .../{id}/pause`, `POST .../{id}/resume`, `POST .../{id}/abort`, `POST /api/v1/operations/rolling/steps/{stepId}/confirm`.

- [ ] **Step 1: DTOs + validator**

`RollingOperationDtos.cs` as above (namespace `MSOSync.Api.Dtos.Operations`; check sibling Dtos folder namespace convention first).

`CreateRollingOperationRequestValidator.cs`:

```csharp
using FluentValidation;
using MSOSync.Api.Dtos.Operations;

namespace MSOSync.Api.Validators;

public sealed class CreateRollingOperationRequestValidator : AbstractValidator<CreateRollingOperationRequest>
{
    private static readonly string[] Kinds = ["RollingMaintenance", "RollingUpgrade"];
    private static readonly string[] WaveActions = ["manual-confirm", "auto-window"];

    public CreateRollingOperationRequestValidator()
    {
        RuleFor(x => x.Kind).Must(k => Kinds.Contains(k))
            .WithMessage("Kind must be RollingMaintenance or RollingUpgrade");
        RuleFor(x => x.NodeIds).NotEmpty();
        RuleForEach(x => x.NodeIds).NotEmpty().MaximumLength(50);
        RuleFor(x => x.WaveSize).GreaterThan(0).When(x => x.WaveSize is not null);
        RuleFor(x => x.WavePercent).InclusiveBetween(1, 100).When(x => x.WavePercent is not null);
        RuleFor(x => x).Must(x => x.WaveSize is not null || x.WavePercent is not null)
            .WithMessage("WaveSize or WavePercent is required");
        RuleFor(x => x.GateSoakSeconds).InclusiveBetween(0, 3600);
        RuleFor(x => x.WaveAction).Must(a => WaveActions.Contains(a))
            .WithMessage("WaveAction must be manual-confirm or auto-window");
        RuleFor(x => x.WindowSeconds).GreaterThan(0)
            .When(x => x.WaveAction == "auto-window")
            .WithMessage("WindowSeconds is required for auto-window");
        RuleFor(x => x.TargetVersion).NotEmpty()
            .When(x => x.Kind == "RollingUpgrade")
            .WithMessage("TargetVersion is required for RollingUpgrade");
        RuleFor(x => x.VerificationTimeoutSeconds).InclusiveBetween(30, 86400);
    }
}
```

- [ ] **Step 2: Failing query-service tests**

`RollingOperationQueryServiceTests.cs` (InMemory context; seed one `SyncOperation` with `MetadataJson = RollingOperationPolicy.ToJson(...)` + 3 steps):

```csharp
[Fact] public async Task GetDetail_returns_operation_with_ordered_steps()
// steps seeded out of order → returned ordered by WaveNumber then NodeId; policy deserialized

[Fact] public async Task GetDetail_unknown_id_throws_not_found()
```

Run: `dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~RollingOperationQueryServiceTests" --nologo`
Expected: FAIL.

- [ ] **Step 3: Implement query service**

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations.Rolling;

public interface IRollingOperationQueryService
{
    Task<RollingOperationDetailDto> GetDetailAsync(Guid operationId, CancellationToken ct = default);
}

public sealed class RollingOperationQueryService(AppDbContext db) : IRollingOperationQueryService
{
    public async Task<RollingOperationDetailDto> GetDetailAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
            ?? throw new NotFoundException($"Operation {operationId} not found", "OPERATION_NOT_FOUND");

        var steps = await db.OperationSteps.AsNoTracking()
            .Where(s => s.OperationId == operationId)
            .OrderBy(s => s.WaveNumber).ThenBy(s => s.NodeId)
            .Select(s => new RollingStepDto(s.StepId, s.NodeId, s.WaveNumber, s.Status,
                s.StartedAt, s.CompletedAt, s.ErrorMessage))
            .ToListAsync(ct);

        return new RollingOperationDetailDto(op.OperationId, op.OperationType, op.Status, op.Result,
            RollingOperationPolicy.FromJson(op.MetadataJson!), steps);
    }
}
```

NOTE: `RollingOperationDetailDto`/`RollingStepDto` reference — these live in `MSOSync.Api.Dtos.Operations` per Step 1, but Metadata cannot reference Api (RULE-ARCH). **Move both records plus `CreateRollingOperationRequest`/`CreateRollingOperationResponse` decision:** domain DTOs (`RollingStepDto`, `RollingOperationDetailDto`) go in `src/MSOSync.Metadata/Operations/Rolling/RollingOperationDtos.cs` (RULE-DTO-3: domain DTOs in Metadata); API request/response records stay in `MSOSync.Api/Dtos/Operations/`. Adjust Step 1 accordingly when implementing.

Run tests: PASS. Register: `services.AddScoped<IRollingOperationQueryService, RollingOperationQueryService>();`

- [ ] **Step 4: Controller**

`src/MSOSync.Api/Controllers/RollingOperationsController.cs` (mirror auth pattern from `NodeLifecycleController`; `Actor` property pattern — copy from that controller):

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Operations;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Rolling;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/operations/rolling")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class RollingOperationsController(
    IRollingOperationService      rolling,
    IRollingOperationQueryService query,
    INodeAuthorizationService     authz) : ControllerBase
{
    private string Actor => User.Identity?.Name ?? "unknown";

    [HttpPost]
    [ProducesResponseType(typeof(CreateRollingOperationResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create([FromBody] CreateRollingOperationRequest req, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var policy = new RollingOperationPolicy(req.WaveSize, req.WavePercent, req.GateSoakSeconds,
            req.WaveAction, req.WindowSeconds, req.TargetVersion, req.VerificationTimeoutSeconds);
        var kind = Enum.Parse<OperationType>(req.Kind);
        var id = await rolling.CreateAsync(kind, req.NodeIds, policy, initiatedBy: null, Actor, ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreateRollingOperationResponse(id));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RollingOperationDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RollingOperationDetailDto>> Get(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        return Ok(await query.GetDetailAsync(id, ct));
    }

    [HttpPost("{id:guid}/pause")]
    [ProducesResponseType(204)] [ProducesResponseType(404)] [ProducesResponseType(409)]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.PauseAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/resume")]
    [ProducesResponseType(204)] [ProducesResponseType(404)] [ProducesResponseType(409)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.ResumeAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/abort")]
    [ProducesResponseType(204)] [ProducesResponseType(404)] [ProducesResponseType(409)]
    public async Task<IActionResult> Abort(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.AbortAsync(id, Actor, ct);
        return NoContent();
    }

    [HttpPost("steps/{stepId:guid}/confirm")]
    [ProducesResponseType(204)] [ProducesResponseType(404)] [ProducesResponseType(409)]
    public async Task<IActionResult> ConfirmStep(Guid stepId, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.ConfirmStepAsync(stepId, ct);
        return NoContent();
    }
}
```

Verify `INodeAuthorizationService` namespace + `Actor` derivation against `NodeLifecycleController` and match (its `Actor` may come from a claim, not `Identity.Name`).

- [ ] **Step 5: Build, full unit run, commit**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
dotnet test tests/MSOSync.MetadataTests --nologo
```

Expected: 0 warnings, green.

```powershell
git add src/MSOSync.Api/ src/MSOSync.Metadata/ tests/MSOSync.MetadataTests/Operations/
git commit -m "feat(2B.1-T5): rolling operations API (create/get/pause/resume/abort/confirm)"
```
