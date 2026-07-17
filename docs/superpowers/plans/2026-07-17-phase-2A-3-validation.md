# Phase 2A.3 — Validation (FluentValidation Exclusively)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the one instance of manual validation in a controller (finding 2A-002: `PreferencesController` validates the `key` parameter with an `if`/`BadRequest` check instead of a FluentValidation validator). Verify the rest of the codebase is clean.

**Architecture:** `PreferencesController.Upsert` validates `key` inline. The fix is to extract this into an `UpsertPreferenceRequest` record that wraps both route parameter and body, then create `UpsertPreferenceRequestValidator`. Since `key` is a route parameter (`[HttpPut("{key}")]`), not a body field, the simplest approach is to keep the route binding but add a custom action filter or validate inline with a named validator. The lowest-friction approach is a small `[FromRoute]` binder with a validator: create `UpsertPreferenceRequest(string Key, JsonElement Value)` and bind both from route + body, then let FluentValidation auto-validate.

However, since FluentValidation auto-validation works on `[FromBody]` parameters, and `key` comes from the route, the practical approach is to inject and call `IValidator<UpsertPreferenceRequest>` explicitly — matching the pattern already used in `AuditController` and `DashboardController`.

**Tech Stack:** C# 13 / .NET 9 / FluentValidation / ASP.NET Core

## Global Constraints

- No new product features. Scope is strictly standardization.
- Definition of Complete: implementation merged + `dotnet test` exits 0 + no new rule violations + docs updated.
- RULE-VAL-1: Every `[FromBody]` and `[FromQuery]` request type has a registered `AbstractValidator<T>`.
- RULE-VAL-2: No if/return-BadRequest validation in controller actions for input shape or format.
- RULE-VAL-3: No DataAnnotation attributes on request DTOs.
- Validators live in `src/MSOSync.Api/Validators/`.
- Do not change the `key` route parameter URL — that would be a breaking API change.

---

## File Map

**Create:**
- `src/MSOSync.Api/Validators/UpsertPreferenceRequestValidator.cs`
- `tests/MSOSync.ApiTests/Validators/UpsertPreferenceRequestValidatorTests.cs` (or nearest test project)

**Modify:**
- `src/MSOSync.Api/Controllers/PreferencesController.cs` — remove inline validation, inject validator
- `docs/architecture/audit-backlog-2A.md` — mark 2A-002 Complete

---

## Task 1: Create UpsertPreferenceRequestValidator

**Files:**
- Create: `src/MSOSync.Api/Validators/UpsertPreferenceRequestValidator.cs`
- Test: `tests/MSOSync.ApiTests/Validators/UpsertPreferenceRequestValidatorTests.cs`

**Interfaces:**
- Produces: `UpsertPreferenceRequestValidator` — validates that `Key` is not empty and ≤ 100 chars

- [ ] **Step 1: Write failing tests**

Find the nearest test project that tests Api validators. Check:
```
ls D:\MSOSync\tests\
```

Look for `MSOSync.ApiTests` or similar. If none exists, use whichever test project is most appropriate. Create the test file:

```csharp
using FluentValidation.TestHelper;
using MSOSync.Api.Validators;
using Xunit;

namespace MSOSync.ApiTests.Validators;

public sealed class UpsertPreferenceRequestValidatorTests
{
    private readonly UpsertPreferenceRequestValidator _sut = new();

    [Fact]
    public void Valid_key_passes()
    {
        var result = _sut.TestValidate("theme");
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_key_fails()
    {
        var result = _sut.TestValidate("");
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorCode("NotEmptyValidator");
    }

    [Fact]
    public void Whitespace_key_fails()
    {
        var result = _sut.TestValidate("   ");
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Key_at_100_chars_passes()
    {
        var key = new string('a', 100);
        var result = _sut.TestValidate(key);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Key_at_101_chars_fails()
    {
        var key = new string('a', 101);
        var result = _sut.TestValidate(key);
        result.ShouldHaveValidationErrorFor(x => x);
    }
}
```

Note: `UpsertPreferenceRequestValidator` validates a `string` (the key) directly — `AbstractValidator<string>`.

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test D:\MSOSync\MSOSync.sln --filter "UpsertPreferenceRequestValidatorTests" -v n
```

Expected: FAIL — `UpsertPreferenceRequestValidator` not found.

- [ ] **Step 3: Create the validator**

Create `src/MSOSync.Api/Validators/UpsertPreferenceRequestValidator.cs`:

```csharp
using FluentValidation;

namespace MSOSync.Api.Validators;

public sealed class UpsertPreferenceRequestValidator : AbstractValidator<string>
{
    public UpsertPreferenceRequestValidator()
    {
        RuleFor(key => key)
            .NotEmpty().WithMessage("Preference key must not be empty.")
            .MaximumLength(100).WithMessage("Preference key must be at most 100 characters.");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test D:\MSOSync\MSOSync.sln --filter "UpsertPreferenceRequestValidatorTests" -v n
```

Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```
git add src/MSOSync.Api/Validators/UpsertPreferenceRequestValidator.cs
git add tests/MSOSync.ApiTests/Validators/UpsertPreferenceRequestValidatorTests.cs
git commit -m "feat(2A.3): add UpsertPreferenceRequestValidator"
```

---

## Task 2: Update PreferencesController to Use Validator

**Files:**
- Modify: `src/MSOSync.Api/Controllers/PreferencesController.cs`

**Interfaces:**
- Consumes: `UpsertPreferenceRequestValidator` from Task 1

Current code to replace (lines 23–28):
```csharp
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
```

- [ ] **Step 1: Update PreferencesController**

```csharp
// src/MSOSync.Api/Controllers/PreferencesController.cs
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Validators;
using MSOSync.Metadata.Preferences;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/preferences")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class PreferencesController(
    IUserPreferencesService            preferencesService,
    UpsertPreferenceRequestValidator   keyValidator)
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
        await keyValidator.ValidateAndThrowAsync(key, ct);
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

`ValidateAndThrowAsync` throws `ValidationException` on failure. `GlobalExceptionHandler` already maps `FluentValidation.ValidationException` to HTTP 400, so no `try/catch` is needed.

`UpsertPreferenceRequestValidator` is registered automatically via `AddValidatorsFromAssemblyContaining<AuthController>()` in Program.cs — no additional DI registration needed. However, it must be injected as a concrete type. Verify Program.cs registers validators from the assembly. If it uses `AddFluentValidationAutoValidation()` + `AddValidatorsFromAssemblyContaining<>()`, direct constructor injection of `UpsertPreferenceRequestValidator` works.

- [ ] **Step 2: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 3: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add src/MSOSync.Api/Controllers/PreferencesController.cs
git commit -m "fix(2A.3-2A-002): PreferencesController uses UpsertPreferenceRequestValidator"
```

---

## Task 3: Final Verification and Audit Backlog Update

- [ ] **Step 1: Scan for remaining manual validation in controllers**

```
grep -rn "return BadRequest" D:\MSOSync\src\MSOSync.Api\Controllers\ --include="*.cs"
```

Review every match. Matches that handle non-input-shape concerns (e.g., business rule violations, not-found conditions) are acceptable. Only `if (input.Invalid) return BadRequest()` for format/shape rules violates RULE-VAL-2.

Expected: No matches for format/shape validation. Any remaining `BadRequest` calls should be for business logic, not input format.

- [ ] **Step 2: Update audit-backlog-2A.md**

In `docs/architecture/audit-backlog-2A.md`, update row 2A-002 from "Not Started" to "Complete".

- [ ] **Step 3: Run full test suite**

```
dotnet test D:\MSOSync\MSOSync.sln
```

Expected: 0 failures.

- [ ] **Step 4: Commit**

```
git add docs/architecture/audit-backlog-2A.md
git commit -m "docs(2A.3): mark 2A-002 Complete"
```

---

## Completion Criteria

2A.3 is **Complete** when:
1. No `if (...) return BadRequest(...)` for input shape/format validation in any controller.
2. `UpsertPreferenceRequestValidator` exists and all 5 unit tests pass.
3. `dotnet test` exits 0.
4. `docs/architecture/audit-backlog-2A.md` has 2A-002 marked Complete.
