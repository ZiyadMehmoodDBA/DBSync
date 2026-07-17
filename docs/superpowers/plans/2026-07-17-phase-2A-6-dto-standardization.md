# Phase 2A.6 — DTO Standardization

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the two DTOs defined inline in `ExportJobController.cs` (`CreateExportJobRequest` and `ExportJobDto`) into dedicated files in `MSOSync.Api/Dtos/Export/`. Verify no other DTOs are defined inside controller files. Document the DTO placement convention.

**Architecture:** Domain DTOs live in `MSOSync.Metadata`. API-specific request/response types live in `MSOSync.Api/Dtos/`. Moving the DTOs is a pure file reorganization — no behavior changes, no namespace changes (keep same namespace as current to avoid touching consumers), no API contract changes.

**Tech Stack:** C# 13 / .NET 9

## Global Constraints

- No new product features. Scope is strictly standardization.
- Definition of Complete: implementation merged + `dotnet test` exits 0 + no new rule violations + docs updated.
- RULE-DTO-1: No DTOs defined inside controller files.
- RULE-DTO-2: No duplicate DTO definitions for the same API resource.
- RULE-DTO-3: Domain DTOs live in `MSOSync.Metadata`. API-specific types in `MSOSync.Api/Dtos/`.
- Do not change DTO field names, types, or namespace — that would be a breaking change.
- `ExportJobDto` is currently referenced in the controller as a return type. Keep the namespace the same after move.

---

## File Map

**Create:**
- `src/MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs`
- `src/MSOSync.Api/Dtos/Export/ExportJobDto.cs`
- `docs/architecture/dto-inventory.md`

**Modify:**
- `src/MSOSync.Api/Controllers/ExportJobController.cs` — remove inline DTO definitions (keep all controller logic)
- `docs/architecture/audit-backlog-2A.md` — mark 2A-003 Complete

---

## Task 1: Move Inline DTOs to Dedicated Files

**Files:**
- Create: `src/MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs`
- Create: `src/MSOSync.Api/Dtos/Export/ExportJobDto.cs`
- Modify: `src/MSOSync.Api/Controllers/ExportJobController.cs`

**Interfaces:**
- Produces: `CreateExportJobRequest` in `MSOSync.Api.Controllers` namespace (keep current namespace to avoid touching consumers)
- Produces: `ExportJobDto` in `MSOSync.Api.Controllers` namespace

Note: The inline records are currently in namespace `MSOSync.Api.Controllers` (they are declared at the bottom of the controller file). When moving to dedicated files, keep them in namespace `MSOSync.Api.Dtos.Export` is cleaner, but check if any other file references these types by namespace. Run a grep first.

- [ ] **Step 1: Check where CreateExportJobRequest and ExportJobDto are referenced**

```
grep -rn "CreateExportJobRequest\|ExportJobDto" D:\MSOSync\src\ --include="*.cs"
```

If only referenced inside `ExportJobController.cs` itself, move to `MSOSync.Api.Dtos.Export` namespace. If referenced in frontend or other files, note that — but since these are server-side DTOs and the frontend uses TypeScript, C# namespace changes don't affect frontend.

- [ ] **Step 2: Create CreateExportJobRequest.cs**

Create `src/MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs`:

```csharp
namespace MSOSync.Api.Dtos.Export;

public sealed record CreateExportJobRequest(
    string ResourceType,
    string Format,
    string FiltersJson,
    Guid?  ParentJobId = null
);
```

- [ ] **Step 3: Create ExportJobDto.cs**

Create `src/MSOSync.Api/Dtos/Export/ExportJobDto.cs`:

```csharp
namespace MSOSync.Api.Dtos.Export;

public sealed record ExportJobDto(
    Guid            JobId,
    Guid?           ParentJobId,
    string          RequestedBy,
    string          ResourceType,
    string          Format,
    string          Status,
    int             ProgressPercent,
    long?           RowCount,
    string?         ErrorMessage,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
```

- [ ] **Step 4: Update ExportJobController to remove inline definitions and add using**

Remove the two inline record declarations at lines 154–175 of `ExportJobController.cs` and add the `using` statement:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Export;
using MSOSync.Common;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/export-jobs")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ExportJobController(
    IExportJobService     jobService,
    ICurrentUserService   currentUser,
    IPermissionService    permissionService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(202)]
    public async Task<IActionResult> CreateJob(
        [FromBody] CreateExportJobRequest request, CancellationToken ct)
    {
        var exportPerms = await permissionService.GetEffectivePermissionsAsync(currentUser.GetCurrentUsername(), ct);
        if (!exportPerms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        if (!string.IsNullOrEmpty(request.FiltersJson))
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                switch (request.ResourceType)
                {
                    case "events":
                        var ef = JsonSerializer.Deserialize<EventFilter>(request.FiltersJson, jsonOptions);
                        if (ef is null) return BadRequest("Invalid filtersJson for events");
                        break;
                    case "incoming-batches":
                        var ibf = JsonSerializer.Deserialize<IncomingBatchFilter>(request.FiltersJson, jsonOptions);
                        if (ibf is null) return BadRequest("Invalid filtersJson for incoming-batches");
                        break;
                    case "audit":
                        var af = JsonSerializer.Deserialize<AuditFilter>(request.FiltersJson, jsonOptions);
                        if (af is null) return BadRequest("Invalid filtersJson for audit");
                        break;
                    default:
                        return BadRequest($"Unknown resourceType: {request.ResourceType}");
                }
            }
            catch (JsonException)
            {
                return BadRequest("Invalid filtersJson: malformed JSON");
            }
        }
        else if (!new[] { "events", "incoming-batches", "audit" }.Contains(request.ResourceType))
        {
            return BadRequest($"Unknown resourceType: {request.ResourceType}");
        }

        var username = currentUser.GetCurrentUsername();
        var job = await jobService.CreateJobAsync(
            username,
            request.ResourceType,
            request.Format,
            request.FiltersJson,
            request.ParentJobId,
            ct);
        return StatusCode(202, new { jobId = job.JobId });
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ExportJobDto>), 200)]
    public async Task<IActionResult> GetJobs(
        [FromQuery] bool all = false, CancellationToken ct = default)
    {
        var username = currentUser.GetCurrentUsername();
        var exportPerms = await permissionService.GetEffectivePermissionsAsync(username, ct);
        if (!exportPerms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        IReadOnlyList<SyncExportJob> jobs;
        if (all)
        {
            if (!exportPerms.Permissions.Contains(SystemPermissions.ManageUsers))
                return Forbid();
            jobs = await jobService.GetAllJobsAsync(ct);
        }
        else
        {
            jobs = await jobService.GetJobsForUserAsync(username, ct);
        }

        return Ok(jobs.Select(ToDto));
    }

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var username = currentUser.GetCurrentUsername();
        var perms = await permissionService.GetEffectivePermissionsAsync(username, ct);
        if (!perms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        var job = await jobService.GetJobAsync(id, ct);
        if (job is null || job.Status is ExportJobStatus.Deleted or ExportJobStatus.Expired)
            return NotFound();

        if (job.RequestedBy != username && !perms.Permissions.Contains(SystemPermissions.ManageUsers))
            return Forbid();

        if (job.OutputPath is null || !System.IO.File.Exists(job.OutputPath))
            return NotFound();

        var contentType = job.Format == "json" ? "application/json" : "text/csv";
        var fileName    = $"{job.ResourceType}-export-{job.JobId}.{job.Format}";
        return PhysicalFile(job.OutputPath, contentType, fileName);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken ct)
    {
        var username = currentUser.GetCurrentUsername();
        var perms = await permissionService.GetEffectivePermissionsAsync(username, ct);
        if (!perms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        var job = await jobService.GetJobAsync(id, ct);
        if (job is null) return NotFound();

        if (job.RequestedBy != username && !perms.Permissions.Contains(SystemPermissions.ManageUsers))
            return Forbid();

        await jobService.SoftDeleteJobAsync(id, ct);
        return NoContent();
    }

    private static ExportJobDto ToDto(SyncExportJob j) => new(
        j.JobId, j.ParentJobId, j.RequestedBy, j.ResourceType, j.Format,
        j.Status, j.ProgressPercent, j.RowCount, j.ErrorMessage,
        j.ExpiresAt, j.CreatedAt, j.StartedAt, j.CompletedAt);
}
```

- [ ] **Step 5: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 6: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```
git add src/MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs
git add src/MSOSync.Api/Dtos/Export/ExportJobDto.cs
git add src/MSOSync.Api/Controllers/ExportJobController.cs
git commit -m "fix(2A.6-2A-003): move CreateExportJobRequest and ExportJobDto out of controller"
```

---

## Task 2: Verify No Other Inline DTOs and Write DTO Inventory

**Files:**
- Create: `docs/architecture/dto-inventory.md`
- Modify: `docs/architecture/audit-backlog-2A.md`

- [ ] **Step 1: Scan for DTOs in controller files**

```
grep -rn "^public sealed record\|^public record\|^public sealed class\|^public class" D:\MSOSync\src\MSOSync.Api\Controllers\ --include="*.cs"
```

Expected: No record or class definitions in controller files (except the controller class itself).

- [ ] **Step 2: Create DTO inventory document**

Create `docs/architecture/dto-inventory.md`:

```markdown
# DTO Inventory

## Placement Convention

| Type | Location |
|---|---|
| Domain data transfer objects (query results, read models) | `src/MSOSync.Metadata/<Feature>/Dtos/` |
| API-specific request types | `src/MSOSync.Api/Dtos/<Feature>/` |
| API-specific response types | `src/MSOSync.Api/Dtos/<Feature>/` |

## Rules

- **RULE-DTO-1:** No DTOs defined inside controller files. All DTOs in `MSOSync.Api/Dtos/` or `MSOSync.Metadata/*/`.
- **RULE-DTO-2:** No duplicate DTO definitions for the same API resource. One canonical location per DTO type.
- **RULE-DTO-3:** Domain DTOs live in `MSOSync.Metadata`. API-specific response wrappers live in `MSOSync.Api/Dtos/`.

## Canonical DTO Locations

| DTO | Namespace | Location |
|---|---|---|
| `CreateExportJobRequest` | `MSOSync.Api.Dtos.Export` | `src/MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs` |
| `ExportJobDto` | `MSOSync.Api.Dtos.Export` | `src/MSOSync.Api/Dtos/Export/ExportJobDto.cs` |
| `NodeDto` | `MSOSync.Metadata.Dtos` | `src/MSOSync.Metadata/Dtos/NodeDto.cs` |
| `HeartbeatRequest` | `MSOSync.Metadata.Dtos` | `src/MSOSync.Metadata/Dtos/HeartbeatRequest.cs` |
| `WorkerStatusDto` | `MSOSync.App.Workers` | `src/MSOSync.App/Workers/WorkerStatusDto.cs` |

*Note: This table lists notable DTOs. Full inventory available via `grep -rn "sealed record\|sealed class" src/ --include="*.cs" | grep -v Controller | grep -v Service | grep -v Repository`.*
```

- [ ] **Step 3: Update audit-backlog-2A.md**

Mark 2A-003 as Complete in `docs/architecture/audit-backlog-2A.md`.

- [ ] **Step 4: Commit**

```
git add docs/architecture/dto-inventory.md
git add docs/architecture/audit-backlog-2A.md
git commit -m "docs(2A.6): DTO inventory and placement convention, mark 2A-003 Complete"
```

---

## Completion Criteria

2A.6 is **Complete** when:
1. `grep -rn "^public sealed record\|^public record" src/MSOSync.Api/Controllers/ --include="*.cs"` returns zero matches.
2. `src/MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs` and `ExportJobDto.cs` exist.
3. `dotnet test` exits 0.
4. `docs/architecture/dto-inventory.md` committed.
5. `docs/architecture/audit-backlog-2A.md` has 2A-003 marked Complete.
