# Epic 13 Final Code Review — Fix Report

**Date:** 2026-07-14
**Branch:** main

## Summary

All 10 Critical/Important/Minor findings addressed in a single commit. Build: 0 warnings, 0 errors. Unit tests: 445 passed. Integration tests (Notification): 9/9 passed.

---

## CRITICAL FIX 1 — Frontend severity filter tabs (A–F)

**Files changed:**
- `src/MSOSync.Metadata/Notifications/INotificationQueryService.cs` — added `string? severityFilter` param to `GetPagedAsync`
- `src/MSOSync.Metadata/Notifications/NotificationQueryService.cs` — applies `severityFilter` WHERE clause after `unreadOnly`
- `src/MSOSync.Api/Controllers/NotificationController.cs` — added `[FromQuery] string? severity` param, passed through to service
- `src/MSOSync.Frontend/src/features/notifications/api.ts` — added `severity?` option to `getNotifications`
- `src/MSOSync.Frontend/src/features/notifications/hooks.ts` — derives `severity` from filter value and passes to API
- `tests/MSOSync.MetadataTests/Notifications/NotificationQueryServiceTests.cs` — updated 4 call sites to pass `null` for `severityFilter`

**Result:** Critical and Security tabs now send `?severity=Critical` / `?severity=Security` to the API.

---

## CRITICAL FIX 2 — Deep-link routing (routing.ts)

**File:** `src/MSOSync.Frontend/src/features/notifications/routing.ts`

Changed `getTargetRoute` to include `entityId` in all paths:
- `Node` → `/operations/nodes/${entityId}`
- `Worker` → `/operations/workers/${entityId}` (was `/operations/health`)
- `Operation` → `/operations/${entityId}` (was `/operations/jobs`)

---

## CRITICAL FIX 3 — NotificationService.CreateAsync transaction safety

**File:** `src/MSOSync.Metadata/Notifications/NotificationService.cs`

Wrapped the notification row + user-notification rows insert in `BeginTransactionAsync` / `CommitAsync`. Dedup and audience-empty early returns remain outside the transaction.

---

## CRITICAL FIX 4 — ResolveUserIdAsync deleted-user safety

**File:** `src/MSOSync.Metadata/Notifications/NotificationQueryService.cs`

Changed `.FirstAsync(ct)` to `.FirstOrDefaultAsync(ct)` with a `NotFoundException` throw on null. GlobalExceptionHandler maps `NotFoundException` to HTTP 404.

---

## IMPORTANT FIX 5 — MediatR handler registration (MetadataServiceExtensions)

**File:** `src/MSOSync.Metadata/MetadataServiceExtensions.cs`

Confirmed `RegisterServicesFromAssemblyContaining<ParameterMetadataService>()` already scans the entire Metadata assembly, covering all `INotificationHandler<T>` implementations. Removed 6 redundant explicit `AddScoped<XHandler>()` calls and their import.

---

## IMPORTANT FIX 6 — PATCH null body / isRead=false validation

**File:** `src/MSOSync.Api/Controllers/NotificationController.cs`

`PatchNotification` now:
- Returns `400` if request body is null
- Returns `400` if `isRead == false` (mark-unread not supported)
- Proceeds to mark-read and returns `200` otherwise

---

## IMPORTANT FIX 7 — MarkAllReadAsync bulk update

**File:** `src/MSOSync.Metadata/Notifications/NotificationQueryService.cs`

Replaced load-all-then-foreach-SaveChanges with `ExecuteUpdateAsync` — single SQL UPDATE statement, no client-side row loading.

---

## IMPORTANT FIX 8 — MarkRead_ValidId_Returns200 assertion strengthened

**File:** `tests/MSOSync.IntegrationTests/Notifications/NotificationControllerTests.cs`

After the POST `/{id}/read`, the test now queries `?unreadOnly=true` and asserts the marked notification ID is absent from the results.

---

## MINOR FIX 9 — Login URL leading slash

**File:** `tests/MSOSync.IntegrationTests/Notifications/NotificationsFixture.cs`

Changed `"api/v1/auth/login"` to `"/api/v1/auth/login"`.

---

## MINOR FIX 10 — NotificationPublisher parallel SignalR pushes

**File:** `src/MSOSync.App/SignalR/NotificationPublisher.cs`

Replaced sequential `foreach` with `Task.WhenAll(evt.UserIds.Select(...))`. Error level changed from `LogError` to `LogWarning` (per-user push failure is non-fatal).

---

## Test Results

| Suite | Result |
|---|---|
| `dotnet build -c Release` | 0 warnings, 0 errors |
| `npx tsc --noEmit` | no errors |
| `npm run build` | succeeded (pre-existing signalr annotation warnings from node_modules) |
| MSOSync.MetadataTests | 404 passed |
| MSOSync.AppTests | 39 passed |
| MSOSync.ArchTests | 2 passed |
| MSOSync.IntegrationTests (Notification filter) | 9/9 passed |

---

# Phase 2C/2D Final Review Fix Report

**Date:** 2026-07-28

## C1+I5: SqlDistributedLockService lazy-insert + M039 migration

**C1**: Added lazy-seed `INSERT … WHERE NOT EXISTS` before the `UPDATE` in `TryAcquireAsync`. Per-node lock keys (`scheduler:SyncJob:<nodeId>`) are now auto-created on first use — fixing the silent Standby failure.

**I5**: Created `M039_WidenLockColumns.cs` migration widening `lock_name`/`lock_owner` from `varchar(50)` to `varchar(200)`. Updated `SyncLockConfiguration.cs` and `AppDbContextModelSnapshot.cs`.

**Test result**: SchedulerTests 62/62 passed. MetadataTests 633/633 passed.

## C2+C6: SchedulerLockFactory scope-per-acquire

**C2**: `SchedulerLockFactory` now depends on `IServiceScopeFactory`. Each `TryAcquireAsync` creates a dedicated `IServiceScope`, resolves `IDistributedLockService`, passes the scope to `SchedulerLockImpl`. Eliminates captive Singleton→Scoped DbContext dependency.

**C6**: `SchedulerLockImpl` private constructor + static `Create(...)` factory ensures renewal Task starts after full construction. `DisposeAsync` now disposes the owned `IServiceScope`.

Updated `SchedulerLockImplTests`, `SchedulerLockFactoryTests`, `SchedulerLockIntegrationTests` to use the new API.

**Test result**: SchedulerTests 62/62 passed.

## C3: BatchController RetryAll lock name

Replaced `IDistributedLockService` + `IOptions<DistributedLockOptions>` with `ISchedulerLockFactory`. `RetryAll` now calls `TryAcquireAsync("RetryJob", ct)`, matching the `scheduler:RetryJob` key used by the background job.

## C4: GetActiveNodeIdsAsync

Added `GetActiveNodeIdsAsync` to `INodeMetadataService` / `NodeMetadataService` — server-side EF Core query returning only `NodeId` for `Active && !MaintenanceMode` nodes. `AdaptivePollingOrchestrator.LoadActiveNodeIdsAsync` uses it instead of `GetNodesAsync()` + client-side filter.

**Test result**: MetadataTests 633/633 passed.

## C5: CLI publish API key

New `PostMultipartAsync` overload in `MsoSyncHttpClient` adds `X-Api-Key` header when apiKey is not null. `PluginPublishCommand.ExecuteAsync` passes `effectiveApiKey` to it.

## I1-I12: Other findings

- **I1**: Path-traversal guard now uses `startsWith(canonicalTemp + sep) || equals(canonicalTemp)`.
- **I2**: Removed high-cardinality `batch_id` tag from `send_ms` and `ack_ms` metric histograms.
- **I4**: `NodeHttpClient` injects `GzipCompressionService`/`BrotliCompressionService` singletons; `TransportServiceExtensions` registers concrete type for DI.
- **I7**: `SchedulerLockSeeder` now rethrows (after `LogCritical`) on startup failure.
- **I10**: Added `InvalidatePluginCache(pluginId)` to `IMarketplaceService`; implemented via `memoryCache.Remove`; called from `MarketplaceController.Install` after success.
- **I11**: `HandlePublishResponse` made async (`await ReadAsStringAsync`).
- **I12**: `SetHandler` uses `InvocationContext`; exit code set via `context.ExitCode` not `Environment.Exit`.

## Test Results

| Suite | Result |
|---|---|
| MSOSync.SchedulerTests | 62/62 passed |
| MSOSync.TransportTests | 37/37 passed |
| MSOSync.MetadataTests | 633/633 passed |
| MSOSync.PluginTests | 178/178 passed |
| MSOSync.CliTests | 71/71 passed |
| vitest plugins/ | 24/24 passed |

## Commit: [see git log]
