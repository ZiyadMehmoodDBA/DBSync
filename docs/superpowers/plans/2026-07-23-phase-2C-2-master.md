# Phase 2C.2 — Plugin Marketplace Backend: Master Plan

**Date:** 2026-07-23
**Spec:** `docs/superpowers/specs/2026-07-23-phase-2C-2-marketplace-backend.md`
**Prerequisite:** Phase 2C.1 complete — `IPluginInstaller` exists in `MSOSync.Plugin`.

---

## Overview

Expose a hosted registry catalog so MSOSync admins can search, inspect, and install plugins from the API. The marketplace is optional: when unconfigured every endpoint returns 503.

## Task Sequence

| # | File | Description |
|---|------|-------------|
| 1 | `2026-07-23-phase-2C-2-task-1-entity-migration.md` | `SyncMarketplaceCache` entity, EF configuration, M037 migration, `AppDbContext` DbSet, persistence test update |
| 2 | `2026-07-23-phase-2C-2-task-2-services.md` | `MarketplaceOptions`, remote registry models, service interfaces, `MarketplaceCacheStore`, `MarketplaceService`, `PluginUpdateService`, `MarketplaceLogEvents` |
| 3 | `2026-07-23-phase-2C-2-task-3-controller.md` | DTOs + validators, `MarketplaceController` (6 endpoints), `appsettings.json` section |
| 4 | `2026-07-23-phase-2C-2-task-4-di-tests.md` | `MarketplaceServiceExtensions`, HTTP client + Polly, `Program.cs` wiring, unit tests, integration tests |

## Architecture Recap

```
MSOSync.Plugin/Marketplace/
  MarketplaceOptions.cs           IOptions<MarketplaceOptions>
  IMarketplaceCacheStore.cs       bridge to Persistence
  IMarketplaceService.cs          catalog operations
  IPluginUpdateService.cs         update check logic
  MarketplaceLogEvents.cs         EventId constants
  Models/
    RegistryPluginEntry.cs
    RegistryVersionEntry.cs
    RegistrySearchResult.cs
    PluginUpdateManifest.cs

MSOSync.Persistence/
  Entities/SyncMarketplaceCache.cs
  Configurations/SyncMarketplaceCacheConfiguration.cs
  Migrations/M037_MarketplaceCache.cs
  Stores/MarketplaceCacheStore.cs

MSOSync.Metadata/Marketplace/
  MarketplaceService.cs           implements IMarketplaceService
  PluginUpdateService.cs          implements IPluginUpdateService

MSOSync.Api/
  Controllers/MarketplaceController.cs
  Dtos/Marketplace/*.cs (9 DTOs + 3 validators)

MSOSync.App/
  MarketplaceServiceExtensions.cs
  (HTTP client registration in Program.cs)
```

## Key Constraints (enforced across all tasks)

- Migration number: **M037** (M035 reserved for 2D.2 lock_expiry, M036 reserved for 2D.3)
- `[GlobalEntity]` on `SyncMarketplaceCache` (no tenant filter)
- `AsNoTracking()` on all EF reads in `MarketplaceCacheStore`
- No `Task.WhenAll` on shared `DbContext` — all `UpsertBulkAsync` and `CheckAllAsync` iterate sequentially
- `[Authorize(Policy = "AdminOnly")]` on controller class
- `[ProducesResponseType]` on every action
- `MarketplaceOptions.IsConfigured` checked first in every action → 503 guard
- Validation via `ValidateAndThrowAsync` (project pattern; `GlobalExceptionHandler` maps `ValidationException` → 400)
- `MSOSync.Metadata` must not reference `MSOSync.Batch` or `MSOSync.Routing`
- `MSOSync.Plugin` must not reference `MSOSync.Persistence`
- HTTP failures in `MarketplaceService` are caught internally and logged at Warning; never thrown to callers
- Version comparison uses `System.Version.TryParse`, not string comparison
- Table count: 48 → 49 (update `SchemaCreated_All48TablesExist` → `SchemaCreated_All49TablesExist`)
