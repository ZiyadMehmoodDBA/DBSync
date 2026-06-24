# Epic 9A — Operational Read APIs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose read-only REST endpoints for Events, IncomingBatches, and BatchErrors so the operations dashboard has stable, query-optimized APIs before any frontend work begins.

**Architecture:** Three thin controllers delegate to three dedicated query services; all queries use `AsNoTracking()` + LINQ projection (no entity materialization); `IErrorSeverityClassifier` derives severity from stored ConflictType in memory after SQL projection; M015 migration adds `create_time` to `sync_batch_error` plus 6 new indexes.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core 9 / EF Core 9.0.0 / FluentValidation 11.11.0 / xUnit 2.9.3 / FluentAssertions 6.12.2 / SQLite (unit tests) / LocalDB (integration tests)

## Global Constraints

- C# 13 / .NET 9 / ASP.NET Core 9 / EF Core 9.0.0 — no version overrides inline in `.csproj`
- `TreatWarningsAsErrors = true` — zero warnings on every build
- Central Package Management: **never** add `Version=` in `.csproj` files
- `AsNoTracking()` on every EF query — no entity tracking
- No entity objects leave query services — DTOs only
- `PagedResult<T>` namespace: `MSOSync.Metadata.Common` (moved from `MSOSync.Metadata.Users`)
- `FluentValidation 11.11.0` — add to `MSOSync.Metadata.csproj` (currently missing)
- `ViewerOrAbove` policy already defined in `SecurityServiceExtensions` — do not redefine
- `IX_sync_data_event_create_time` already exists (SyncDataEventConfiguration) — do not recreate
- `IX_sync_data_event_batch_event_id` not needed — PK on `(event_id, batch_id)` covers event_id lookups
- Unit tests: SQLite `DataSource=:memory:` via `TestDbContext.Create()` — NOT EF InMemory
- Integration tests: LocalDB — follow `UsersFixture.cs` pattern exactly

---

## File Map

### New files
```
src/MSOSync.Metadata/Common/PagedResult.cs
src/MSOSync.Metadata/Events/EventSummaryDto.cs
src/MSOSync.Metadata/Events/EventDetailDto.cs
src/MSOSync.Metadata/Events/EventFilter.cs
src/MSOSync.Metadata/Events/EventFilterValidator.cs
src/MSOSync.Metadata/Events/IEventQueryService.cs
src/MSOSync.Metadata/Events/EventQueryService.cs
src/MSOSync.Metadata/IncomingBatches/IncomingBatchSummaryDto.cs
src/MSOSync.Metadata/IncomingBatches/IncomingBatchDetailDto.cs
src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs
src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilterValidator.cs
src/MSOSync.Metadata/IncomingBatches/IIncomingBatchQueryService.cs
src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs
src/MSOSync.Metadata/BatchErrors/ErrorSeverity.cs
src/MSOSync.Metadata/BatchErrors/IErrorSeverityClassifier.cs
src/MSOSync.Metadata/BatchErrors/ErrorSeverityClassifier.cs
src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryDto.cs
src/MSOSync.Metadata/BatchErrors/BatchErrorDetailDto.cs
src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryCountDto.cs
src/MSOSync.Metadata/BatchErrors/BatchErrorFilter.cs
src/MSOSync.Metadata/BatchErrors/BatchErrorFilterValidator.cs
src/MSOSync.Metadata/BatchErrors/IBatchErrorQueryService.cs
src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs
src/MSOSync.Api/Controllers/EventsController.cs
src/MSOSync.Api/Controllers/IncomingBatchesController.cs
src/MSOSync.Api/Controllers/BatchErrorsController.cs
src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.cs
src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.Designer.cs  ← auto-generated
tests/MSOSync.MetadataTests/Events/EventFilterValidatorTests.cs
tests/MSOSync.MetadataTests/Events/EventQueryServiceTests.cs
tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchFilterValidatorTests.cs
tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchQueryServiceTests.cs
tests/MSOSync.MetadataTests/BatchErrors/BatchErrorFilterValidatorTests.cs
tests/MSOSync.MetadataTests/BatchErrors/ErrorSeverityClassifierTests.cs
tests/MSOSync.MetadataTests/BatchErrors/BatchErrorQueryServiceTests.cs
tests/MSOSync.IntegrationTests/OperationalRead/OperationalReadFixture.cs
tests/MSOSync.IntegrationTests/OperationalRead/EventsTests.cs
tests/MSOSync.IntegrationTests/OperationalRead/IncomingBatchesTests.cs
tests/MSOSync.IntegrationTests/OperationalRead/BatchErrorsTests.cs
```

### Modified files
```
src/MSOSync.Metadata/Users/PagedResult.cs                            ← DELETE (replaced by Common/)
src/MSOSync.Metadata/Users/UsersManagementService.cs                 ← update using
src/MSOSync.Metadata/Users/IUsersManagementService.cs                ← update using
src/MSOSync.Metadata/MSOSync.Metadata.csproj                         ← add FluentValidation package
src/MSOSync.Metadata/MetadataServiceExtensions.cs                    ← add DI registrations
src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs                 ← add FluentValidation arm
src/MSOSync.Persistence/Entities/SyncBatchError.cs                   ← add CreateTime property
src/MSOSync.Persistence/Configurations/SyncBatchErrorConfiguration.cs ← add CreateTime + index
src/MSOSync.Persistence/Configurations/SyncDataEventConfiguration.cs  ← add 3 new indexes
src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs ← add 3 new indexes
src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs       ← auto-updated by ef tooling
tests/MSOSync.IntegrationTests/Users/UsersTests.cs                   ← update PagedResult namespace
```

---

## Tasks

| # | Task file | Deliverable |
|---|---|---|
| 1 | [task-1](2026-06-24-epic9a-task-1-dtos-filters-validators.md) | PagedResult refactor + all DTOs + filter classes + filter validators |
| 2 | [task-2](2026-06-24-epic9a-task-2-error-severity-classifier.md) | ErrorSeverity enum + IErrorSeverityClassifier + ErrorSeverityClassifier |
| 3 | [task-3](2026-06-24-epic9a-task-3-event-query-service.md) | IEventQueryService + EventQueryService + SQLite unit tests |
| 4 | [task-4](2026-06-24-epic9a-task-4-incoming-batch-query-service.md) | IIncomingBatchQueryService + IncomingBatchQueryService + SQLite unit tests |
| 5 | [task-5](2026-06-24-epic9a-task-5-batch-error-query-service.md) | IBatchErrorQueryService + BatchErrorQueryService + SQLite unit tests |
| 6 | [task-6](2026-06-24-epic9a-task-6-controllers.md) | 3 controllers + GlobalExceptionHandler FluentValidation arm |
| 7 | [task-7](2026-06-24-epic9a-task-7-m015-migration.md) | SyncBatchError entity/config + M015 migration + AddMetadata() wiring |
| 8 | [task-8](2026-06-24-epic9a-task-8-unit-tests.md) | Comprehensive unit test pass — all query services |
| 9 | [task-9](2026-06-24-epic9a-task-9-integration-tests.md) | OperationalReadFixture + 3 integration test classes |

---

## Build Verification

After Task 9 completes, run from repo root:

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests\MSOSync.MetadataTests -c Debug --logger "console;verbosity=normal"
dotnet test tests\MSOSync.IntegrationTests --filter "FullyQualifiedName~OperationalRead" -c Debug --logger "console;verbosity=normal"
```

Expected: build clean (zero warnings), all MetadataTests green, all OperationalRead integration tests green.

---

## Spec Reference

Full design: `docs/superpowers/specs/2026-06-24-epic9a-operational-read-apis-design.md`
