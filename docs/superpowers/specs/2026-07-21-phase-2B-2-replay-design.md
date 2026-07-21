# Phase 2B.2 — Batch Replay Engine Design

**Date:** 2026-07-21
**Phase:** 2B.2 (follows 2B.1 rolling operations)
**Status:** Approved

---

## Goal

Give operators a durable, cancellable replay operation that covers two scenarios:

1. **FailedDelivery** — bulk re-send outgoing batches that are stuck in Error/Failed state
2. **MissedData** — re-create and deliver batches for a node that was offline/draining and missed events entirely

Both modes produce a `SyncOperation` (type `BatchReplay`) visible in the Jobs page with per-item progress, full audit trail, and SignalR live updates.

---

## Scope Decisions

1. **Routing uses current config.** When re-creating batches for missed events, the hub applies today's router/channel assignments — not historical config at event time. Simpler; consistent with how new events are routed.
2. **Items enumerated at creation time.** `ReplayOperationService.CreateAsync` queries candidate batches/events immediately and writes one `SyncReplayItem` per batch. Operator sees item count before the worker starts. Zero items → operation completes immediately with result `NoData`.
3. **Selection scope: node + channel + time range + optional batch IDs.** Cherry-pick by batch ID is only available in `FailedDelivery` mode.
4. **Max range: 90 days** (configurable via `ReplayOptions.MaxRangeDays`). Prevents runaway queries.
5. **Concurrent replays: max 5** (configurable). Worker advances all running operations per tick.
6. **No new SyncNode state.** Replay is an operation, not a lifecycle transition.

---

## Data Model

### Migration: M034_BatchReplay

**`sync_replay_request`** — replay parameters, 1:1 with SyncOperation:

| Column | Type | Notes |
|---|---|---|
| `replay_id` | uniqueidentifier | PK |
| `operation_id` | uniqueidentifier | FK → sync_operation CASCADE |
| `node_id` | varchar(50) | target node |
| `channel_ids_json` | nvarchar(max) | JSON string[]; NULL = all channels |
| `batch_ids_json` | nvarchar(max) | JSON long[]; NULL = no cherry-pick; FailedDelivery only |
| `from_time` | datetime2 | inclusive |
| `to_time` | datetime2 | inclusive |
| `replay_mode` | varchar(20) | `FailedDelivery` \| `MissedData` \| `Both` |
| `tenant_id` | uniqueidentifier | NOT NULL |

**`sync_replay_item`** — one row per batch being replayed:

| Column | Type | Notes |
|---|---|---|
| `item_id` | uniqueidentifier | PK |
| `operation_id` | uniqueidentifier | FK → sync_operation CASCADE |
| `source_batch_id` | bigint | NULL for MissedData (new batch); existing batch_id for FailedDelivery |
| `replay_batch_id` | bigint | NULL until worker creates the new batch (MissedData) or confirms reset (FailedDelivery) |
| `node_id` | varchar(50) | denormalised for query |
| `channel_id` | varchar(50) | |
| `event_count` | int | |
| `status` | varchar(20) | `Pending` \| `Processing` \| `Completed` \| `Failed` \| `Skipped` |
| `error_message` | nvarchar(1000) | NULL |
| `tenant_id` | uniqueidentifier | NOT NULL |

**Indexes:**
- `ix_sync_replay_item_op_status` on `(operation_id, status)`
- `ix_sync_replay_item_tenant_node` on `(tenant_id, node_id)`

**Enum additions:**
- `OperationType.BatchReplay` (existing enum in `MSOSync.Metadata`)
- New `ReplayMode` enum: `FailedDelivery`, `MissedData`, `Both`
- New `ReplayItemStatus` enum: `Pending`, `Processing`, `Completed`, `Failed`, `Skipped`

No changes to `SyncOperation` columns — `BatchReplay` reuses `status`, `progress`, `result`, `initiatedBy`, `startedAt`, `completedAt`.

---

## Service Layer

All new files under `src/MSOSync.Metadata/Operations/Replay/`.

### `IReplayOperationService` (command)

```csharp
Task<ReplayOperationCreatedDto> CreateAsync(CreateReplayRequest req, CancellationToken ct);
Task CancelAsync(Guid operationId, CancellationToken ct);
```

**`CreateAsync` flow:**
1. Validate node exists + is `Active` or `Draining` (Disabled/Decommissioned → `OperationStateException` → 409)
2. Validate `FromTime < ToTime` and range ≤ `ReplayOptions.MaxRangeDays`
3. Validate `BatchIds` only provided when `ReplayMode = FailedDelivery`
4. Create `SyncOperation` (type `BatchReplay`, status `Pending`)
5. Create `SyncReplayRequest`
6. Enumerate items (see below) → write `SyncReplayItem` rows (all status `Pending`)
7. If zero items → set operation `Completed` + result `NoData`, return early
8. Publish `OperationChangedEvent` (MediatR → SignalR)
9. Return `{ OperationId, ItemCount }`

**`CancelAsync`:** Set operation `Cancelled`; all `Pending` items → `Skipped`. Publish event.

### Item Enumeration (called from `CreateAsync`)

**FailedDelivery:** Query `SyncOutgoingBatch` WHERE `node_id = req.NodeId AND status IN (Error, Failed) AND created_at BETWEEN FromTime AND ToTime AND (channel_id IN ChannelIds OR ChannelIds null) AND (batch_id IN BatchIds OR BatchIds null)`. One `SyncReplayItem` per row; `source_batch_id` = existing batch id.

**MissedData:** Query `SyncDataEvent` rows in time range. For each event, resolve target node via `IRoutingService.ResolveAsync` (current config). Keep only events where resolved node = `req.NodeId` AND no successful `SyncOutgoingBatch` exists for this node+event. **One `SyncReplayItem` per channel** covering all matching events in the window; `source_batch_id` = NULL. Worker calls `IBatchCreator` which handles internal batch-size chunking.

**Both:** Union of both queries, deduplicated on `(channel_id, event range)`.

### `IReplayOperationQueryService` (read)

```csharp
Task<ReplayOperationDetailDto?> GetDetailAsync(Guid operationId, CancellationToken ct);
Task<CursorPageResult<ReplayItemDto>> GetItemsAsync(Guid operationId, ReplayItemFilter filter, CancellationToken ct);
```

### `IReplayWorkerService` (internal, called by worker)

```csharp
Task AdvanceAsync(CancellationToken ct);
```

---

## Background Worker

**`ReplayWorker`** in `src/MSOSync.App/Workers/`, follows `RollingOperationWorker` pattern exactly.

```csharp
public sealed class ReplayWorker(IServiceScopeFactory scopeFactory,
    IOptions<ReplayOptions> opts, IWorkerStatusRegistry registry,
    ILogger<ReplayWorker> logger) : BackgroundService
```

- Registers as `"ReplayWorker"` in `StartAsync`
- `PeriodicTimer` interval: `ReplayOptions.WorkerIntervalSeconds` (default 10s)
- Scoped `IReplayWorkerService` per tick (stateless)

**Advance tick logic:**

1. Transition `Pending` BatchReplay operations → `Running` (up to `MaxConcurrentOperations`)
2. For each `Running` operation: fetch `SyncReplayRequest` + next page of `Pending` items (page size `ItemPageSize`, default 50)
3. Per item:
   - **FailedDelivery** (`source_batch_id` NOT NULL): load `SyncOutgoingBatch`, set `status = Retry`; mark item `Completed`
   - **MissedData** (`source_batch_id` NULL): call `IBatchCreator.CreateBatchAsync(nodeId, channelId, events)`; store returned id in `replay_batch_id`; mark item `Completed`; on exception → item `Failed` + `error_message`
   - **Either mode**: if node is no longer `Active` at execution time → item `Skipped`
4. Update `SyncOperation.Progress = completedCount * 100 / totalItems`
5. If no more `Pending` items: set operation `Completed`; result = `Success` if no failures, `PartialFailure` if any `Failed`
6. Publish `OperationChangedEvent` after each page

**Restart safety:** Each item is saved individually. Worker re-entering after interruption skips `Completed`/`Failed`/`Skipped` items automatically.

---

## API

**`ReplayController`** at `/api/v1/operations/replay`, permission: `ManageNodeLifecycle`.

```
POST   /api/v1/operations/replay              → 201 { operationId, itemCount }
GET    /api/v1/operations/replay/{id}         → 200 ReplayOperationDetailDto
GET    /api/v1/operations/replay/{id}/items   → 200 CursorPageResult<ReplayItemDto>
POST   /api/v1/operations/replay/{id}/cancel  → 204
```

### Request DTO

```csharp
public sealed record CreateReplayOperationRequest(
    string NodeId,
    string ReplayMode,      // "FailedDelivery" | "MissedData" | "Both"
    DateTime FromTime,
    DateTime ToTime,
    string[]? ChannelIds,   // null = all channels
    long[]? BatchIds);      // null = no cherry-pick; FailedDelivery only
```

### Validator (`CreateReplayOperationRequestValidator`)

- `NodeId` not empty
- `ReplayMode` one of `FailedDelivery`, `MissedData`, `Both`
- `FromTime < ToTime`
- `(ToTime - FromTime).TotalDays <= ReplayOptions.MaxRangeDays`
- `BatchIds` must be null/empty when `ReplayMode != FailedDelivery`

### Response DTOs (in `MSOSync.Metadata`)

```csharp
public sealed record ReplayOperationCreatedDto(Guid OperationId, int ItemCount);

public sealed record ReplayOperationDetailDto(
    Guid OperationId, string Status, string? Result,
    string NodeId, string ReplayMode,
    DateTime FromTime, DateTime ToTime,
    string[]? ChannelIds, long[]? BatchIds,
    int TotalItems, int CompletedItems, int FailedItems, int SkippedItems,
    DateTime? StartedAt, DateTime? CompletedAt);

public sealed record ReplayItemDto(
    Guid ItemId, string NodeId, string ChannelId,
    int EventCount, string Status, string? ErrorMessage,
    long? SourceBatchId, long? ReplayBatchId);
```

### `ReplayOptions`

```csharp
public sealed class ReplayOptions
{
    public int MaxRangeDays { get; init; } = 90;
    public int WorkerIntervalSeconds { get; init; } = 10;
    public int MaxConcurrentOperations { get; init; } = 5;
    public int ItemPageSize { get; init; } = 50;
}
```

Bound to `"Replay"` config section. Registered in `AddMetadata()`. Added to `appsettings.json`.

---

## Frontend

### Jobs Page Changes (`JobsPage.tsx`)

- Add `BatchReplay` to `ALL_TYPES`, `TYPE_BADGE_COLORS` (indigo)
- "New Replay" button alongside "New Rolling Operation"
- Row click on `BatchReplay` → opens `ReplayDetailPanel`

### `ReplayWizard` (4 steps)

1. **Mode** — radio: Failed Delivery / Missed Data / Both
2. **Target** — single node selector (searchable dropdown) + optional channel multi-select
3. **Time Range** — `fromTime` / `toTime` date-time pickers; client-side guard: range ≤ 90 days
4. **Review** — summary card + optional batch ID textarea (comma-separated; FailedDelivery mode only); "Start Replay" submit

On submit: `POST /api/v1/operations/replay` → close wizard → toast "Replay started — {N} items queued"

### `ReplayDetailPanel`

- Header: node ID, mode badge, time range, progress bar (`completedItems / totalItems`)
- Status summary: Pending / Processing / Completed / Failed / Skipped counts
- Items AG Grid: Channel | Events | Status | Source Batch | Replay Batch | Error
- 5s polling via `useReplayOperation(id)`
- Cancel button (if Running/Pending) → `POST /{id}/cancel`

### New Files

```
src/MSOSync.Frontend/src/shared/types/replay.ts
src/MSOSync.Frontend/src/shared/api/replay.ts
src/MSOSync.Frontend/src/shared/hooks/useReplayOperations.ts
src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayWizard.tsx
src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayDetailPanel.tsx
src/MSOSync.Frontend/src/features/operations/jobs/components/__tests__/ReplayWizard.test.tsx
```

### `eventRouter.ts` update

`OperationChanged` case: add `queryClient.invalidateQueries({ queryKey: ['replay-operations'] })` alongside existing rolling-operations invalidation.

---

## Testing

### Unit Tests (`MSOSync.MetadataTests/Operations/Replay/`)

`ReplayOperationServiceTests` (~14 tests):
- `CreateAsync_ActiveNode_ValidRange_Returns_OperationWithItems`
- `CreateAsync_DrainingNode_ValidRange_Returns_OperationWithItems`
- `CreateAsync_DisabledNode_Throws_OperationStateException`
- `CreateAsync_RangeExceeds90Days_Throws_ValidationException`
- `CreateAsync_NoMatchingBatches_Completes_Immediately_NoData`
- `CreateAsync_BatchIds_Only_Allowed_For_FailedDelivery_Mode`
- `CreateAsync_FailedDelivery_Items_Have_SourceBatchId_Set`
- `CreateAsync_MissedData_Items_Have_SourceBatchId_Null`
- `CancelAsync_Running_Sets_Cancelled_Skips_Pending_Items`
- `CancelAsync_Completed_Throws_OperationStateException`

`ReplayWorkerServiceTests` (~12 tests):
- `Advance_FailedDelivery_Item_Resets_Batch_To_Retry`
- `Advance_MissedData_Item_Calls_BatchCreator_Stores_ReplayBatchId`
- `Advance_NodeNoLongerActive_Skips_Item`
- `Advance_BatchCreator_Throws_Marks_Item_Failed`
- `Advance_AllItemsComplete_Sets_Operation_Completed_Success`
- `Advance_SomeItemsFailed_Sets_PartialFailure`
- `Advance_Resumable_After_Interruption_Skips_Completed_Items`
- `Advance_Pages_50_Items_Per_Tick`
- `Advance_Transitions_Pending_Operation_To_Running`
- `Advance_MaxConcurrentOperations_Respected`

### App Tests (`MSOSync.AppTests/`)

- `ReplayWorker_Registers_With_IWorkerStatusRegistry`

### Integration Tests (`MSOSync.IntegrationTests/Operations/ReplayApiTests.cs`)

Uses `[Collection("Lifecycle")]`:
- `Create_replay_FailedDelivery_returns_201_with_item_count`
- `Get_replay_returns_detail_with_items`
- `Create_replay_invalid_mode_returns_400`
- `Create_replay_range_too_large_returns_400`
- `Create_replay_disabled_node_returns_409`
- `Cancel_replay_returns_204_and_status_Cancelled`
- `Replay_endpoints_without_permission_return_403`

### Migration Tests (`M034MigrationTests.cs`)

Own LocalDB database (`MSOSyncM034_Test`). Verifies:
- `sync_replay_request` table exists with required columns
- `sync_replay_item` table exists with required columns
- Expected indexes present

### Frontend Tests (`ReplayWizard.test.tsx`, ~5 tests)

- Renders step 1 mode selection
- Advances through all 4 steps
- Batch IDs field hidden when mode ≠ FailedDelivery
- Date range > 90 days shows validation error
- Submit calls `createReplay` with correct payload

---

## File Map

### New — Backend

```
src/MSOSync.Persistence/Entities/SyncReplayRequest.cs
src/MSOSync.Persistence/Entities/SyncReplayItem.cs
src/MSOSync.Persistence/Configurations/SyncReplayRequestConfiguration.cs
src/MSOSync.Persistence/Configurations/SyncReplayItemConfiguration.cs
src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs
src/MSOSync.Persistence/Migrations/M034_BatchReplay.Designer.cs
src/MSOSync.Metadata/Operations/Replay/ReplayMode.cs
src/MSOSync.Metadata/Operations/Replay/ReplayItemStatus.cs
src/MSOSync.Metadata/Operations/Replay/IReplayOperationService.cs
src/MSOSync.Metadata/Operations/Replay/ReplayOperationService.cs
src/MSOSync.Metadata/Operations/Replay/IReplayOperationQueryService.cs
src/MSOSync.Metadata/Operations/Replay/ReplayOperationQueryService.cs
src/MSOSync.Metadata/Operations/Replay/IReplayWorkerService.cs
src/MSOSync.Metadata/Operations/Replay/ReplayWorkerService.cs
src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayOperationCreatedDto.cs
src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayOperationDetailDto.cs
src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayItemDto.cs
src/MSOSync.Metadata/Operations/Replay/Dtos/CreateReplayRequest.cs
src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayItemFilter.cs
src/MSOSync.Metadata/Options/ReplayOptions.cs
src/MSOSync.Api/Controllers/ReplayController.cs
src/MSOSync.Api/Dtos/Requests/CreateReplayOperationRequest.cs
src/MSOSync.Api/Validators/CreateReplayOperationRequestValidator.cs
src/MSOSync.App/Workers/ReplayWorker.cs
```

### Modified — Backend

```
src/MSOSync.Persistence/AppDbContext.cs                         (add DbSet<SyncReplayRequest>, DbSet<SyncReplayItem>)
src/MSOSync.Metadata/Operations/OperationType.cs               (add BatchReplay)
src/MSOSync.Metadata/Extensions/MetadataServiceExtensions.cs   (register new services + ReplayOptions)
src/MSOSync.App/Extensions/AppServiceExtensions.cs             (register ReplayWorker)
src/MSOSync.App/appsettings.json                               (add "Replay": {} section)
```

### New — Frontend

```
src/MSOSync.Frontend/src/shared/types/replay.ts
src/MSOSync.Frontend/src/shared/api/replay.ts
src/MSOSync.Frontend/src/shared/hooks/useReplayOperations.ts
src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayWizard.tsx
src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayDetailPanel.tsx
src/MSOSync.Frontend/src/features/operations/jobs/components/__tests__/ReplayWizard.test.tsx
```

### Modified — Frontend

```
src/MSOSync.Frontend/src/features/operations/jobs/JobsPage.tsx
src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts
```

### New — Tests

```
tests/MSOSync.MetadataTests/Operations/Replay/ReplayOperationServiceTests.cs
tests/MSOSync.MetadataTests/Operations/Replay/ReplayWorkerServiceTests.cs
tests/MSOSync.IntegrationTests/Operations/ReplayApiTests.cs
tests/MSOSync.IntegrationTests/Lifecycle/M034MigrationTests.cs
```

### Modified — Tests

```
tests/MSOSync.AppTests/Workers/ReplayWorkerTests.cs  (new file, AppTests project)
```

---

## Global Constraints

- All Phase 2A rules apply: named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3
- RULE-CTL-2: `ReplayController` must not inject `AppDbContext` directly
- Worker interval from `IOptions<ReplayOptions>` — no hardcoded values
- Migration numbering: **M034** (`src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs`)
- All work commits to `main`
