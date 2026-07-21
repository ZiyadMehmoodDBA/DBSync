# Pull Request

## Summary

<!-- What does this PR change and why? -->

## Stabilization Rules Checklist (Phase 2A)

Full rule definitions: `docs/superpowers/specs/2026-07-17-phase-2A-platform-stabilization.md`

### API
- [ ] **RULE-API-1:** Every controller action has `[ProducesResponseType]` for each possible status code.
- [ ] **RULE-API-2:** No anonymous objects in controller responses — named record/class types only.
- [ ] **RULE-API-3:** Error responses use `ProblemDetails` via `GlobalExceptionHandler`. No manual error JSON.

### Error Handling
- [ ] **RULE-ERR-1:** No controller catches an exception type already handled by `GlobalExceptionHandler`.
- [ ] **RULE-ERR-2:** New domain exception types are registered in `GlobalExceptionHandler`.
- [ ] **RULE-ERR-3:** `X-Correlation-Id` present on all 4xx/5xx responses.

### Validation
- [ ] **RULE-VAL-1:** Every `[FromBody]`/`[FromQuery]` request type has a registered `AbstractValidator<T>`.
- [ ] **RULE-VAL-2:** No if/return-BadRequest shape/format validation in controller actions.
- [ ] **RULE-VAL-3:** No DataAnnotation attributes on request DTOs — FluentValidation only.

### Dependency Injection
- [ ] **RULE-DI-1:** No singleton injects a scoped service without `IServiceScopeFactory`.
- [ ] **RULE-DI-2:** `IServiceProvider` constructor injection has an inline justification comment.
- [ ] **RULE-DI-3:** No `IgnoreQueryFilters()` outside `IPlatformRepository<T>` implementations.

### DTOs
- [ ] **RULE-DTO-1:** No DTOs defined inside controller files.
- [ ] **RULE-DTO-2:** No duplicate DTO definitions for the same API resource.
- [ ] **RULE-DTO-3:** Domain DTOs in `MSOSync.Metadata`; API response wrappers in `MSOSync.Api/Dtos/`.

### Controllers
- [ ] **RULE-CTL-1:** No business logic in controllers (bind → authorize → call service → map response).
- [ ] **RULE-CTL-2:** No database calls in controllers.
- [ ] **RULE-CTL-3:** No loops/calculations beyond simple pagination bounds in controllers.

### Logging
- [ ] **RULE-LOG-1:** Structured message templates with named placeholders — no string interpolation.
- [ ] **RULE-LOG-2:** No `Console.Write*` / `Debug.Write*` in non-test code.
- [ ] **RULE-LOG-3:** Correlation ID flows through all HTTP request log entries.

### Configuration
- [ ] **RULE-CFG-1:** Configuration accessed via `IOptions<T>` — no `IConfiguration.GetValue()` in services/workers.
- [ ] **RULE-CFG-2:** Required configuration validated at startup (fail-fast).
- [ ] **RULE-CFG-3:** No hardcoded tunables in service code — all in `appsettings.json`.

### Background Workers
- [ ] **RULE-WRK-1:** Recurring `BackgroundService` registers with `IWorkerStatusRegistry`.
- [ ] **RULE-WRK-2:** Recurring `BackgroundService` uses `PeriodicTimer`.
- [ ] **RULE-WRK-3:** Recurring `BackgroundService` calls `RecordTickStart()` / `RecordTickComplete()` / `RecordTickFailed()`.

### Tests
- [ ] **RULE-TEST-1:** No `BackgroundService.StartAsync` in unit tests — test tick methods directly.
- [ ] **RULE-TEST-2:** Unit tests mock all external dependencies (no real DB, no HTTP).
- [ ] **RULE-TEST-3:** Integration tests use `Testcontainers.MsSql` inside existing integration test projects.

## Test Plan

- [ ] `dotnet test D:\MSOSync\MSOSync.sln` — all assemblies green (only accepted environmental failures per `docs/architecture/test-infrastructure.md`)
