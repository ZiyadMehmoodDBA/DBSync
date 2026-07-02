# Task 2: Backend PreferencesController

**Part of:** Epic 11E — User Preferences & Saved Workspaces  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11e-user-preferences-design.md`  
**Depends on:** Task 1 (`IUserPreferencesService` must exist)

## Files

**Create:**
- `src/MSOSync.Api/Controllers/PreferencesController.cs`

No new test file — the controller is thin delegation; coverage comes from service tests (Task 1) and the build passing with auth policies applied.

## Interfaces Consumed (from Task 1)

```csharp
IUserPreferencesService:
  Task<Dictionary<string, JsonElement>> GetAllAsync(CancellationToken ct)
  Task UpsertAsync(string key, JsonElement value, CancellationToken ct)
  Task BulkUpsertAsync(Dictionary<string, JsonElement> preferences, CancellationToken ct)
  Task DeleteAsync(string key, CancellationToken ct)
```

## Endpoints Produced (consumed by Task 3 frontend)

```
GET    /api/v1/preferences           → 200 { "page.events.pageSize": 25, ... }
PUT    /api/v1/preferences/{key}     → 200  (body: any JSON value)
PUT    /api/v1/preferences           → 200  (body: { key: value, ... })
DELETE /api/v1/preferences/{key}     → 204
```

---

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true`
- Auth policy `"ViewerOrAbove"` on all actions (class-level attribute)
- `[FromBody] JsonElement` for value parameters — do NOT use `object` or `dynamic`

---

- [ ] **Step 1: Create PreferencesController**

```csharp
// src/MSOSync.Api/Controllers/PreferencesController.cs
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Preferences;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/preferences")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class PreferencesController(IUserPreferencesService preferencesService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, JsonElement>), 200)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await preferencesService.GetAllAsync(ct));

    [HttpPut("{key}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Upsert(string key, [FromBody] JsonElement value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            return BadRequest(new { code = "INVALID_KEY", message = "Preference key must be 1–100 characters." });
        await preferencesService.UpsertAsync(key, value, ct);
        return Ok();
    }

    [HttpPut]
    [ProducesResponseType(200)]
    public async Task<IActionResult> BulkUpsert(
        [FromBody] Dictionary<string, JsonElement> preferences,
        CancellationToken ct)
    {
        await preferencesService.BulkUpsertAsync(preferences, ct);
        return Ok();
    }

    [HttpDelete("{key}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        await preferencesService.DeleteAsync(key, ct);
        return NoContent();
    }
}
```

- [ ] **Step 2: Build clean**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.Api -c Debug --warnaserror 2>&1 | Select-Object -Last 5
dotnet build src/MSOSync.App -c Debug --warnaserror 2>&1 | Select-Object -Last 5
```

Expected: Build succeeded, 0 warning(s) for both projects.

- [ ] **Step 3: Verify controller is discovered**

The controller will be auto-discovered by `AddControllers()` in Program.cs — no additional DI registration needed. Confirm `IUserPreferencesService` is registered (done in Task 1 Step 9). No additional step required.

- [ ] **Step 4: Run full MetadataTests to confirm nothing broken**

```pwsh
dotnet test tests/MSOSync.MetadataTests -c Debug 2>&1 | Select-Object -Last 10
```

Expected: all tests pass (171+ tests, 0 failed).

- [ ] **Step 5: Commit**

```pwsh
git add src/MSOSync.Api/Controllers/PreferencesController.cs

git commit -m "feat(11e): add PreferencesController — GET/PUT/PUT-bulk/DELETE /api/v1/preferences"
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Tests: <N> passed, 0 failed
Concerns: <none or list>
```
