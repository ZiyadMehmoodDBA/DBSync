# Phase 2A — Platform Stabilization

**Status:** Approved specification — 2026-07-17
**Phase:** 2A (prerequisite for all Phase 2B+ work)
**Roadmap:** `docs/superpowers/specs/2026-07-17-roadmap-v2.md`

---

## Goal

Freeze the MSOSync architecture before adding new capabilities. Phase 2A produces a stable, consistent, documented codebase baseline that all subsequent phases build on. No new features ship until Phase 2A exits.

## Architecture

Phase 2A is a stabilization pass, not a rewrite. Work proceeds in three stages:

1. **Audit** — Scan every layer of the stack against the stabilization rules below. Every finding gets a tracking ID, severity, file:line, current behavior, and recommended change.
2. **Triage** — Every audit item must be resolved: fixed, explicitly deferred (with reason and target milestone), or explicitly accepted (with rationale).
3. **Enforce** — Stabilization rules become code-review gates. New PRs that violate these rules are blocked.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / Entity Framework Core / React 19 / TypeScript / Serilog / FluentValidation / MediatR / SignalR

---

## Global Constraints

- No new product features during Phase 2A. Scope is strictly stabilization and consistency.
- Every audit finding must have an explicit resolution before Phase 2A can exit.
- Stabilization rules defined in this spec become mandatory after Phase 2A closes. Violations block PR merge.
- Breaking API changes require an API versioning strategy before implementation.
- The `appsettings.Development.json` file must never contain plaintext credentials. Use environment variables or secrets management.

---

## Workstreams

### 2A.1 — API Standardization

**Goal:** Every HTTP response from every controller uses a single, documented response contract.

**In Scope:**
- Audit all controllers in `MSOSync.Api/Controllers/` for response shape consistency
- Define `ApiResponse<T>` wrapper (or confirm bare-resource pattern as the standard — pick one)
- Document which HTTP status codes map to which response shapes
- Apply consistent `[ProducesResponseType]` attributes across all controller actions
- Fix identified anomalies (see Audit Findings section)

**Out of Scope:**
- API versioning implementation (that is 2A.8)
- Changing business logic in controllers
- Frontend changes driven by response shape changes

**Deliverables:**
- API response contract document
- All controllers annotated with `[ProducesResponseType]`
- OpenAPI spec reflects accurate response types

**Definition of Done:**
- Zero controllers returning anonymous objects
- Zero controllers returning response shapes not covered by the API contract
- All `[ProducesResponseType]` annotations present and accurate

---

### 2A.2 — Error Handling

**Goal:** Single, consistent exception pipeline with no per-controller fallback handling for domain exceptions already covered by the global handler.

**In Scope:**
- Audit all try-catch blocks in controllers
- Identify which catches handle exceptions already covered by `GlobalExceptionHandler`
- Remove redundant catches; replace with reliance on the global handler
- Verify global handler covers all domain exception types
- Ensure `X-Correlation-Id` header is returned on all error responses

**Out of Scope:**
- Adding new exception types for Phase 2B+ features
- Frontend error display changes

**Deliverables:**
- Rationalized exception hierarchy
- GlobalExceptionHandler handles all domain exceptions
- No duplicate exception mapping in controllers
- Exception-to-status-code mapping table documented

**Definition of Done:**
- No controller catches an exception type already handled by `GlobalExceptionHandler`
- All domain exception types mapped in `GlobalExceptionHandler`
- `X-Correlation-Id` present on all 4xx/5xx responses

---

### 2A.3 — Validation

**Goal:** FluentValidation exclusively. No manual validation in controllers or services.

**In Scope:**
- Audit all controllers and request DTOs for manual validation (if/return BadRequest patterns)
- Create missing FluentValidation validators for any request type lacking one
- Ensure `AddFluentValidationAutoValidation()` is wired and working for all routes
- Remove manual validation that duplicates validator logic

**Out of Scope:**
- Changing validation rules (only standardizing how validation is expressed)
- Domain-level invariant checks in services (those are correct; not controllers' job)

**Deliverables:**
- One validator per request DTO in `MSOSync.Api/Validators/`
- No manual if/BadRequest validation in controllers

**Definition of Done:**
- Zero manual validation in controller actions for input shape/format rules
- Every `[FromBody]` request type has a registered FluentValidation validator
- `PreferencesController` key validation moved to `UpsertPreferenceValidator`

---

### 2A.4 — Dependency Injection

**Goal:** No scoped services captured by singletons. No raw `IServiceProvider` use outside of documented, justified locations.

**In Scope:**
- Audit all `AddSingleton` registrations in Program.cs and all `*ServiceExtensions.cs` files
- Verify no singleton injects a scoped service directly (without `IServiceScopeFactory`)
- Document the two justified `IServiceProvider` usages (OperationService keyed dispatch, PluginServicesAdapter bridge)
- Add a comment to each justified location explaining why service locator is acceptable

**Out of Scope:**
- Changing the plugin isolation architecture
- Changing the operation handler dispatch pattern

**Deliverables:**
- DI lifetime audit table (service → lifetime → dependencies)
- All singleton/scoped mismatches resolved
- Justified `IServiceProvider` usages documented inline

**Definition of Done:**
- Zero singleton services injecting scoped services without `IServiceScopeFactory`
- All `IServiceProvider` injection sites have a comment explaining justification
- DI lifetime audit table committed to `docs/architecture/`

---

### 2A.5 — Architecture Consistency

**Goal:** Controllers are thin. No business logic in controllers. No duplicate services doing the same thing.

**In Scope:**
- Audit controllers for business logic that belongs in services
- Audit services for duplication (two services doing the same domain operation)
- Move any presentation-only helpers (markdown builders, DTO mappers) to appropriate locations

**Out of Scope:**
- Refactoring that changes external API behavior
- Merging services with different responsibilities

**Deliverables:**
- Controllers contain only: input binding, authorization checks, service delegation, response mapping
- No duplicate service implementations identified
- Service responsibility map committed to `docs/architecture/`

**Definition of Done:**
- No loops, calculations, or domain decisions in controller action methods
- No two services implement the same domain operation independently

---

### 2A.6 — DTO Standardization

**Goal:** Every DTO has a single canonical definition. No DTOs defined inline in controllers. Clear naming convention.

**In Scope:**
- Audit all DTOs across `MSOSync.Api/Dtos/`, `MSOSync.Metadata/*/Dtos/`, inline in controllers
- Move inline DTOs to dedicated files
- Identify and resolve any true duplicates (same shape, different name, different namespace)
- Document DTO placement convention: domain DTOs in `MSOSync.Metadata`, API-specific DTOs in `MSOSync.Api/Dtos/`

**Out of Scope:**
- Changing DTO field names or types (breaking change — requires API versioning)
- Frontend DTO/type changes unless directly caused by API shape changes

**Deliverables:**
- DTO inventory: every DTO with its canonical location
- `ExportJobController` inline DTOs moved to `MSOSync.Api/Dtos/Export/`
- No DTOs defined inside controller files

**Definition of Done:**
- Zero DTOs defined in controller files
- Zero duplicate DTO definitions for the same API response shape
- DTO placement convention documented

---

### 2A.7 — Logging

**Goal:** Structured logging exclusively. No string interpolation in log messages. Consistent log levels.

**In Scope:**
- Audit all log calls for string interpolation (use Serilog message templates, not `$""`)
- Audit log levels — are debug/info/warning/error used consistently?
- Verify no `Console.WriteLine` in production code paths
- Confirm correlation IDs flow through all log entries for a given HTTP request

**Out of Scope:**
- Adding new log entries for Phase 2B+ features
- Changing log sinks or output format

**Deliverables:**
- Zero string interpolation in log message templates
- Log level guide documented
- Correlation ID confirmed in structured log output

**Definition of Done:**
- All `ILogger` calls use structured parameters: `logger.LogX("Message {Param}", value)`
- No `Console.Write*` or `Debug.Write*` in non-test code
- Correlation ID present in all HTTP request log entries

---

### 2A.8 — Configuration

**Goal:** All configuration accessed via typed `IOptions<T>`. No raw `IConfiguration.GetValue()` in application services or workers.

**In Scope:**
- Audit all `IConfiguration.GetValue()` and `config.GetSection()` calls in application code
- Create missing typed options classes (`HeartbeatOptions`, `LifecycleOptions` etc.)
- Register options via `Configure<T>()` in Program.cs
- Replace raw config access in workers with injected `IOptions<T>`
- Workers affected: `ProbeWorker`, `HeartbeatWorker`, `ConnectivityEvaluator`, `PullJob`, `SyncJob`

**Out of Scope:**
- Changing configuration schema (only standardizing access pattern)
- `appsettings.*.json` structure changes

**Deliverables:**
- `HeartbeatOptions` class covering all heartbeat/probe configuration keys
- `LifecycleOptions` class (or confirm existing one covers scheduler config)
- All scheduler workers use `IOptions<T>` instead of raw `IConfiguration`

**Definition of Done:**
- Zero `IConfiguration.GetValue()` calls in `MSOSync.Scheduler/` workers
- All configuration accessed via typed options in application services and workers
- `appsettings.json` keys validated at startup (fail-fast on missing required config)

---

### 2A.9 — Background Services

**Goal:** All background workers follow the same pattern. All registered with `IWorkerStatusRegistry`. All use `PeriodicTimer` for scheduling.

**In Scope:**
- Audit all classes inheriting `BackgroundService` in the solution
- Verify each worker calls `registry.Register()`, `RecordTickStart()`, `RecordTickComplete()`, `RecordTickFailed()`
- Verify all recurring workers use `PeriodicTimer` (not `Task.Delay` loops)
- Confirm `AdminBootstrapper` one-shot pattern is documented (exempt from tick recording)

**Out of Scope:**
- Adding new workers
- Changing worker execution logic

**Deliverables:**
- Worker inventory: every `BackgroundService` subclass with its pattern compliance status
- Any non-compliant workers brought into compliance
- Pattern documented in `docs/architecture/background-workers.md`

**Definition of Done:**
- Every recurring worker registered with `IWorkerStatusRegistry`
- Every recurring worker uses `PeriodicTimer`
- `AdminBootstrapper` documented as exempt (one-shot, no tick recording required)

---

### 2A.10 — Test Infrastructure

**Goal:** Test coverage baseline established. Critical domain paths have unit and integration tests. Test infrastructure supports future expansion.

**In Scope:**
- Audit current test coverage across all test projects
- Identify critical untested paths in: node lifecycle, sync pipeline, tenant isolation, notification delivery
- Add missing tests for critical paths
- Standardize test project structure and naming
- Ensure all tests pass in CI before Phase 2A exits

**Out of Scope:**
- Load tests, chaos tests (Phase 2I)
- UI automation tests (Phase 2I)
- Frontend component tests (Phase 2I)

**Deliverables:**
- Test coverage report
- Critical path test gap list
- Missing tests added for identified gaps
- All existing tests passing

**Definition of Done:**
- All existing tests pass (`dotnet test` returns 0)
- Coverage report generated
- No critical domain path (node lifecycle, sync pipeline, tenant isolation) has zero test coverage

---

## Audit Methodology

Audit proceeds layer by layer:

```
Controllers (MSOSync.Api/Controllers/)
    ↓
Request/Response DTOs (MSOSync.Api/Dtos/, inline in controllers)
    ↓
Validators (MSOSync.Api/Validators/)
    ↓
Services (MSOSync.Metadata/*/, MSOSync.Engine/)
    ↓
Repositories (MSOSync.Persistence/, platform repos)
    ↓
DbContext + EF Configuration (MSOSync.Persistence/)
    ↓
Middleware (MSOSync.Api/Middleware/, GlobalExceptionHandler)
    ↓
SignalR Hubs (MSOSync.App/Hubs/)
    ↓
Background Workers (MSOSync.Scheduler/Workers/, MSOSync.App/Workers/)
    ↓
Plugin Host (MSOSync.Plugin/)
    ↓
Program.cs + DI Registration
    ↓
Frontend API layer (MSOSync.Frontend/src/shared/api/)
```

Every finding is recorded in the audit backlog with these fields:

| Field | Description |
|---|---|
| **ID** | `2A-NNN` sequential identifier |
| **Severity** | High / Medium / Low |
| **Category** | API / ErrorHandling / Validation / DI / Architecture / DTO / Logging / Configuration / Workers / Tests |
| **Workstream** | 2A.1–2A.10 |
| **File** | Relative path from solution root |
| **Line** | Approximate line number |
| **Current Behavior** | What the code does now |
| **Recommended Change** | What it should do |
| **Breaking?** | Yes / No — does changing this affect API consumers? |
| **Priority** | P1 (must fix before 2A exits) / P2 (should fix) / P3 (deferred) |
| **Resolution** | Fixed / Deferred (milestone) / Accepted (rationale) |

---

## Stabilization Rules

These become mandatory code-review gates after Phase 2A closes. New code violating these rules is blocked at PR review.

### API Rules
- **RULE-API-1:** Every controller action has `[ProducesResponseType]` for each possible status code.
- **RULE-API-2:** No anonymous objects in controller responses. All response bodies use named record or class types.
- **RULE-API-3:** Error responses use `ProblemDetails` format via `GlobalExceptionHandler`. No manual error JSON construction.

### Error Handling Rules
- **RULE-ERR-1:** No controller catches an exception type already handled by `GlobalExceptionHandler`.
- **RULE-ERR-2:** Every new domain exception type is registered in `GlobalExceptionHandler` before shipping.
- **RULE-ERR-3:** `X-Correlation-Id` is present on all 4xx and 5xx responses.

### Validation Rules
- **RULE-VAL-1:** Every `[FromBody]` and `[FromQuery]` request type has a registered `AbstractValidator<T>`.
- **RULE-VAL-2:** No if/return-BadRequest validation in controller actions for input shape or format. Use validators.
- **RULE-VAL-3:** No DataAnnotation attributes on request DTOs. FluentValidation only.

### DI Rules
- **RULE-DI-1:** No singleton service injects a scoped service without `IServiceScopeFactory`.
- **RULE-DI-2:** No `IServiceProvider` injected as a constructor parameter unless the use is documented inline with a justification comment.
- **RULE-DI-3:** No `IgnoreQueryFilters()` called outside `IPlatformRepository<T>` implementations.

### DTO Rules
- **RULE-DTO-1:** No DTOs defined inside controller files. All DTOs in `MSOSync.Api/Dtos/` or `MSOSync.Metadata/*/`.
- **RULE-DTO-2:** No duplicate DTO definitions for the same API resource. One canonical location per DTO type.
- **RULE-DTO-3:** Domain DTOs live in `MSOSync.Metadata`. API-specific response wrappers live in `MSOSync.Api/Dtos/`.

### Controller Rules
- **RULE-CTL-1:** No business logic in controllers. Controllers: bind input → check authorization → call service → map response.
- **RULE-CTL-2:** No database calls in controllers. All DB access via injected services.
- **RULE-CTL-3:** No loops or calculations beyond simple pagination bounds in controllers.

### Logging Rules
- **RULE-LOG-1:** All `ILogger` calls use structured message templates with named placeholders. No string interpolation.
- **RULE-LOG-2:** No `Console.Write*` or `Debug.Write*` in non-test code.
- **RULE-LOG-3:** Correlation ID flows through all HTTP request log entries.

### Configuration Rules
- **RULE-CFG-1:** All configuration in application services and workers accessed via `IOptions<T>`. No `IConfiguration.GetValue()`.
- **RULE-CFG-2:** Required configuration validated at startup. Missing required config causes `InvalidOperationException` at boot, not at first use.
- **RULE-CFG-3:** No hardcoded configuration values in service code. All tunables in `appsettings.json`.

### Background Worker Rules
- **RULE-WRK-1:** Every recurring `BackgroundService` registers with `IWorkerStatusRegistry`.
- **RULE-WRK-2:** Every recurring `BackgroundService` uses `PeriodicTimer` for scheduling.
- **RULE-WRK-3:** Every recurring `BackgroundService` calls `RecordTickStart()`, `RecordTickComplete()`, and `RecordTickFailed()`.

---

## Audit Findings (Current State — 2026-07-17)

The following findings were identified in the initial audit. All are starting-point findings; the full audit may uncover additional items.

| ID | Severity | Category | Workstream | File | ~Line | Issue | Recommendation | Breaking? | Priority |
|---|---|---|---|---|---|---|---|---|---|
| 2A-001 | Low | API | 2A.1 | MSOSync.Api/Controllers/ExportJobController.cs | 73 | Returns `new { jobId = job.JobId }` anonymous object on 202 Accepted | Create `CreateExportJobResponse` record in `MSOSync.Api/Dtos/Export/` | No | P2 |
| 2A-002 | Low | Validation | 2A.3 | MSOSync.Api/Controllers/PreferencesController.cs | 25 | Manual `if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return BadRequest()` | Create `UpsertPreferenceValidator` with FluentValidation | No | P2 |
| 2A-003 | Low | DTO | 2A.6 | MSOSync.Api/Controllers/ExportJobController.cs | 154 | `CreateExportJobRequest` and `ExportJobDto` defined inline in controller file | Move to `MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs` and `ExportJobDto.cs` | No | P2 |
| 2A-004 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/Workers/ProbeWorker.cs | 36 | `config.GetValue<int>("Heartbeat:ProbeIntervalSeconds", 60)` direct config access | Create `HeartbeatOptions` typed options class, inject `IOptions<HeartbeatOptions>` | No | P1 |
| 2A-005 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/Workers/HeartbeatWorker.cs | 42 | `_config.GetValue<int>("Heartbeat:IntervalSeconds", 30)` direct config access | Use `IOptions<HeartbeatOptions>` | No | P1 |
| 2A-006 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs | 63 | Raw `IConfiguration` for Heartbeat config values | Use `IOptions<HeartbeatOptions>` | No | P1 |
| 2A-007 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/PullJob.cs | — | Raw `IConfiguration` for scheduler timing config | Create `SchedulerOptions` typed options, inject `IOptions<SchedulerOptions>` | No | P1 |
| 2A-008 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/SyncJob.cs | — | Raw `IConfiguration` for scheduler timing config | Use `IOptions<SchedulerOptions>` | No | P1 |

**Confirmed clean (no findings):**
- Error handling: `GlobalExceptionHandler` covers all domain exceptions; controller catches are specific and non-redundant
- DI lifetimes: No singleton/scoped mismatches (WorkerStatusRegistry fixed in commit 33f5db1)
- Duplicate DTOs: No conflicting definitions across namespaces
- Controller business logic: Controllers are thin; no DB calls or domain logic
- Logging: Structured logging exclusively; no string interpolation; no Console.Write
- Background workers: All recurring workers follow IWorkerStatusRegistry + PeriodicTimer pattern
- EF tenant filters: IgnoreQueryFilters gated exclusively to PlatformRepository
- Frontend API: Single Axios client, consistent API module pattern, no raw fetch calls

---

## Deliverables

| Deliverable | Location | Owner |
|---|---|---|
| Audit backlog (all findings with IDs) | `docs/architecture/audit-backlog-2A.md` | Phase 2A |
| API response contract document | `docs/architecture/api-response-contract.md` | 2A.1 |
| Exception hierarchy and mapping table | `docs/architecture/exception-hierarchy.md` | 2A.2 |
| DTO inventory and placement convention | `docs/architecture/dto-inventory.md` | 2A.6 |
| DI lifetime audit table | `docs/architecture/di-lifetime-audit.md` | 2A.4 |
| Service responsibility map | `docs/architecture/service-map.md` | 2A.5 |
| Worker inventory and compliance status | `docs/architecture/background-workers.md` | 2A.9 |
| Test coverage report | `docs/architecture/test-coverage.md` | 2A.10 |
| Log level guide | `docs/architecture/logging-guide.md` | 2A.7 |
| Stabilization rules (as PR checklist) | `.github/PULL_REQUEST_TEMPLATE.md` | Cross-cutting |

---

## Audit Item Status Model

Every audit finding and implementation task carries exactly one status at all times:

| Status | Meaning |
|---|---|
| **Not Started** | Identified; no implementation begun |
| **In Progress** | Active work underway |
| **Complete** | Implemented, verified, and meets Definition of Complete |
| **Deferred** | Approved for a later phase — must include target milestone and reason |
| **Rejected** | Intentionally not changing — must include rationale |

No finding may remain without a status. "Known issues" without a decision block Phase 2A exit.

---

## Definition of Complete

A Phase 2A task is **Complete** only when ALL four conditions are met:

1. **Implementation complete** — the change is in the codebase and merged to main.
2. **Automated tests pass** — `dotnet test` exits 0; the change does not break any existing test.
3. **No new rule violations introduced** — the implementation does not itself violate any stabilization rule from this spec.
4. **Documentation updated** — if the change affects a public contract (API shape, configuration key, DTO name, exception type), the corresponding architecture document in `docs/architecture/` is updated.

A task that passes implementation but fails any of the other three conditions is **In Progress**, not Complete.

---

## Exit Criteria

Phase 2A is complete only when ALL of the following are true:

1. Every audit finding has one of: **Fixed**, **Deferred** (with target milestone), or **Accepted** (with rationale). No "known issues" without a decision.
2. All P1 findings are **Fixed**.
3. All stabilization rules are documented and enforced (PR template updated).
4. All existing tests pass (`dotnet test` exits 0).
5. Test coverage report generated and committed.
6. All architecture documents listed in Deliverables are committed to `docs/architecture/`.
7. No controller contains anonymous object responses.
8. No worker uses raw `IConfiguration` access.
9. No DTOs defined in controller files.
10. No singleton injects scoped services without `IServiceScopeFactory`.

Phase 2B work may not begin until all exit criteria are met.
