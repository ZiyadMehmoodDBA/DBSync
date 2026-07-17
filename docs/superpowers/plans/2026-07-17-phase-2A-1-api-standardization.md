# Phase 2A.1 — API Standardization

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix finding 2A-001 (`ExportJobController.CreateJob` returns anonymous `new { jobId }` instead of a named type on 202 Accepted). Audit all controllers for missing `[ProducesResponseType]` annotations. Document the API response contract.

**Architecture:** The codebase already uses a consistent bare-resource pattern (no `ApiResponse<T>` wrapper — controllers return the DTO directly). This plan confirms and documents that pattern, fixes the one anonymous-object response, and ensures all controller actions have accurate `[ProducesResponseType]` attributes. No response shape changes — only annotation and documentation work.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / Swashbuckle OpenAPI

## Global Constraints

- No new product features. Scope is strictly standardization.
- Definition of Complete: implementation merged + `dotnet test` exits 0 + no new rule violations + docs updated.
- RULE-API-1: Every controller action has `[ProducesResponseType]` for each possible status code.
- RULE-API-2: No anonymous objects in controller responses.
- RULE-API-3: Error responses use `ProblemDetails` format via `GlobalExceptionHandler`.
- Do not change response shapes — only add annotations and fix the one anonymous object.
- 2A.6 DTO Standardization must complete before this plan (CreateExportJobRequest and ExportJobDto must be in their dedicated files).

---

## File Map

**Create:**
- `src/MSOSync.Api/Dtos/Export/CreateExportJobResponse.cs` — named response type for 202 Accepted
- `docs/architecture/api-response-contract.md` — API response contract document

**Modify:**
- `src/MSOSync.Api/Controllers/ExportJobController.cs` — replace anonymous response with `CreateExportJobResponse`
- Potentially many controllers — add missing `[ProducesResponseType]` attributes
- `docs/architecture/audit-backlog-2A.md` — mark 2A-001 Complete

---

## Task 1: Fix Anonymous Response in ExportJobController (2A-001)

**Files:**
- Create: `src/MSOSync.Api/Dtos/Export/CreateExportJobResponse.cs`
- Modify: `src/MSOSync.Api/Controllers/ExportJobController.cs`

**Interfaces:**
- Consumes: `MSOSync.Api.Dtos.Export` namespace (already imported after 2A.6)
- Produces: `CreateExportJobResponse` record used in CreateJob action

- [ ] **Step 1: Create CreateExportJobResponse**

Create `src/MSOSync.Api/Dtos/Export/CreateExportJobResponse.cs`:

```csharp
namespace MSOSync.Api.Dtos.Export;

public sealed record CreateExportJobResponse(Guid JobId);
```

- [ ] **Step 2: Update ExportJobController.CreateJob**

In `src/MSOSync.Api/Controllers/ExportJobController.cs`, change line:
```csharp
return StatusCode(202, new { jobId = job.JobId });
```
To:
```csharp
return StatusCode(202, new CreateExportJobResponse(job.JobId));
```

Also update the `[ProducesResponseType]` attribute on `CreateJob`:
```csharp
[HttpPost]
[ProducesResponseType(typeof(CreateExportJobResponse), 202)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
```

- [ ] **Step 3: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/MSOSync.Api/Dtos/Export/CreateExportJobResponse.cs
git add src/MSOSync.Api/Controllers/ExportJobController.cs
git commit -m "fix(2A.1-2A-001): ExportJobController returns CreateExportJobResponse on 202"
```

---

## Task 2: Audit All Controllers for Missing ProducesResponseType

**Files:**
- Modify: Any controllers missing `[ProducesResponseType]` annotations

- [ ] **Step 1: Scan for actions without ProducesResponseType**

```
grep -rn "\[Http" D:\MSOSync\src\MSOSync.Api\Controllers\ --include="*.cs" -A 1 | grep -v "ProducesResponseType\|^\-\-$" | grep "\[Http"
```

This finds HTTP method attributes not followed by a `[ProducesResponseType]`. Review the output manually — some patterns (like `[HttpGet]` for simple list endpoints) may just need the return type added.

Alternatively, open each controller and verify all public action methods have `[ProducesResponseType]` for:
- Happy path (200, 201, 202, 204)
- Business error paths (400, 403, 404, 409) where applicable
- Never add `[ProducesResponseType(500)]` — 500s are handled by GlobalExceptionHandler

- [ ] **Step 2: Add missing annotations**

For each controller action missing annotations, add them. The pattern:

```csharp
// List endpoint
[HttpGet]
[ProducesResponseType(typeof(IReadOnlyList<ChannelDto>), 200)]
public async Task<IActionResult> GetAll(...)

// Create endpoint
[HttpPost]
[ProducesResponseType(typeof(ChannelDto), 201)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
public async Task<IActionResult> Create(...)

// Delete endpoint
[HttpDelete("{id}")]
[ProducesResponseType(204)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
public async Task<IActionResult> Delete(...)
```

Audit each controller file in `src/MSOSync.Api/Controllers/` and add any missing attributes. Keep them minimal — only add annotations for responses the action actually returns.

- [ ] **Step 3: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 4: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```
git add src/MSOSync.Api/Controllers/
git commit -m "fix(2A.1): add missing ProducesResponseType annotations to all controllers"
```

---

## Task 3: Write API Response Contract Document and Update Audit Backlog

**Files:**
- Create: `docs/architecture/api-response-contract.md`
- Modify: `docs/architecture/audit-backlog-2A.md`

- [ ] **Step 1: Create api-response-contract.md**

Create `docs/architecture/api-response-contract.md`:

```markdown
# API Response Contract

MSOSync uses a **bare-resource** response pattern. Controllers return
the DTO directly — there is no `ApiResponse<T>` envelope wrapper.

## Status Code Mapping

| Status | When Used | Body |
|---|---|---|
| 200 OK | Successful GET, PUT | Resource DTO or collection |
| 201 Created | Resource created synchronously | Created resource DTO |
| 202 Accepted | Async operation enqueued | `{ id }` of created job |
| 204 No Content | DELETE, bulk PUT | Empty |
| 400 Bad Request | Validation failure, invalid input | `ProblemDetails` |
| 401 Unauthorized | Missing or invalid JWT | `ProblemDetails` |
| 403 Forbidden | Insufficient permissions | `ProblemDetails` |
| 404 Not Found | Resource does not exist | `ProblemDetails` |
| 409 Conflict | Duplicate entity, concurrency conflict | `ProblemDetails` |
| 500 Internal Server Error | Unhandled exception | `ProblemDetails` |

## Error Responses

All error responses use RFC 7807 `ProblemDetails` format, produced by
`GlobalExceptionHandler` in `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`.

Error response structure:
```json
{
  "timestamp": "2026-07-17T12:00:00Z",
  "status": 400,
  "error": "Validation Failed",
  "code": "VALIDATION_ERROR",
  "message": "NodeId is required",
  "correlationId": "abc123"
}
```

All 4xx and 5xx responses include the `X-Correlation-Id` response header.

## Rules

- **RULE-API-1:** Every controller action has `[ProducesResponseType]` for each possible status code.
- **RULE-API-2:** No anonymous objects in controller responses. All response bodies use named record or class types.
- **RULE-API-3:** Error responses use `ProblemDetails` format via `GlobalExceptionHandler`. No manual error JSON construction.
```

- [ ] **Step 2: Update audit-backlog-2A.md**

Mark 2A-001 as Complete in `docs/architecture/audit-backlog-2A.md`.

- [ ] **Step 3: Commit**

```
git add docs/architecture/api-response-contract.md
git add docs/architecture/audit-backlog-2A.md
git commit -m "docs(2A.1): API response contract document, mark 2A-001 Complete"
```

---

## Completion Criteria

2A.1 is **Complete** when:
1. `grep -rn "new {" src/MSOSync.Api/Controllers/ --include="*.cs"` returns zero matches (no anonymous objects in responses).
2. All controller action methods have `[ProducesResponseType]` for all response codes they return.
3. `dotnet test` exits 0.
4. `docs/architecture/api-response-contract.md` committed.
5. `docs/architecture/audit-backlog-2A.md` has 2A-001 marked Complete.
