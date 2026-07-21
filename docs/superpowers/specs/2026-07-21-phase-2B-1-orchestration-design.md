# Phase 2B.1 — Node Operations Orchestration (Design)

Date: 2026-07-21
Status: Approved (design sections reviewed interactively)
Parent: Phase 2B — Enterprise Operations (`docs/superpowers/specs/2026-07-17-roadmap-v2.md`)

## Purpose

Deliver the operational-orchestration slice of Phase 2B: standalone node drain
mode, rolling node maintenance, and rolling upgrades — one orchestration engine,
three operations. Also absorbs deferred audit finding 2A-029 (direct
`AppDbContext` use in three controllers).

## Phase 2B Decomposition (context)

Roadmap v2 lists 13 Phase 2B modules. Gap analysis against shipped epics:

| Module | Status |
|---|---|
| Live topology visualization | Done (Epics 11A/11B + 12B recolor + 11C SignalR) |
| Cluster operations dashboard | Done (Epic 12C Overview + Jobs) |
| Audit explorer | Done (Epic 11D + 9D audit APIs) |
| Operations timeline | Done (Epic 12C CorrelationTimeline) |
| Cluster diagnostics | Done, basic (Epic 12C Diagnostics) |
| Cluster health monitoring | Partial (worker-level in 12C; node rollup → 2B.3) |
| Configuration comparison | Partial (drift in 12B-2; diff views → 2B.3) |
| Rolling node maintenance | **2B.1 (this spec)** |
| Rolling upgrades | **2B.1 (this spec)** |
| Node drain mode | **2B.1 (this spec)** |
| Sync / batch / event replay | 2B.2 Replay Engine (separate spec) |
| Disaster recovery dashboard | 2B.3 Resilience Dashboards (separate spec) |

Sub-project order: 2B.1 → 2B.2 → 2B.3, each with its own spec → plan → build.

## Scope Decisions

1. **Rolling upgrades = orchestrated windows only.** The hub tracks each node's
   agent version and orchestrates wave-by-wave maintenance windows, verifying
   the version bump after each wave. Binary/package delivery is out of scope
   (operator or package manager installs on the node).
2. **Drain is a new lifecycle state**, not a flag: `Draining`, a reversible
   sibling of `Decommissioning`. Maintenance remains an orthogonal flag on
   `SyncNode` (existing 12B design: "never a lifecycle state").
3. **Rolling execution is durable**: a step table plus a restart-safe
   `BackgroundService` worker following the 12C `sync_operation` pattern —
   no fire-and-forget `Task.Run`, no external scheduler library.

## Domain Model

### Lifecycle state machine

New state `Draining` in `NodeLifecycleState` and
`NodeLifecycleStateMachine` transitions (all via the existing
`NodeLifecycleService.ExecuteTransitionAsync` pipeline — lock, validate,
transaction, post-commit MediatR publish):

- `Active → Draining` — command `StartDrain`
- `Draining → Active` — command `Resume`
- `Draining → Decommissioning` — allowed (drain work already done)

Behavior while `Draining`:

- Routing / `SmartTransportService` stops assigning new outgoing batches to the
  node; in-flight batches complete normally.
- Heartbeats still accepted (204) — the node stays connected.
- When the node's outgoing queue empties, a `DRAIN_COMPLETED` lifecycle-history
  row is written and a SignalR event published. The state remains `Draining`
  (quiesced) until an explicit `Resume`. Drain-completion detection runs in the
  `RollingOperationWorker` tick, which scans **all** `Draining` nodes —
  operation-managed and standalone drains alike (no separate service).

Maintenance flag is unchanged; rolling operations compose it:
drain → set `MaintenanceMode` → work happens → clear flag → `Resume`.

### Agent version

- `SyncNode.AgentVersion` (`string?`) — reported by the node in an extended
  heartbeat payload. Feeds rolling-upgrade verification and the Nodes grid.

### Operation steps

New table `sync_operation_step` (tenant-scoped):

| Column | Notes |
|---|---|
| `step_id` (PK) | |
| `operation_id` (FK → `sync_operation`) | |
| `node_id` | |
| `wave_number` | |
| `status` | `Pending, Draining, InMaintenance, AwaitingVerification, Completed, Failed, Skipped` |
| `started_at`, `completed_at` | |
| `error_message` | |
| `tenant_id` | ITenantScoped |

`SyncOperation.OperationType` gains `RollingMaintenance` and `RollingUpgrade`.
`MetadataJson` holds the wave policy: wave size (count or percent), health-gate
soak duration, wave action (`manual-confirm` or `auto-window` + duration), and
target version (upgrade only).

### Migration

M033: add `agent_version` to `sync_node`; create `sync_operation_step`
(+ tenant composite index). `Draining` transitions are code-only.

## Orchestration Engine

### RollingOperationService (MSOSync.Metadata)

- `CreateAsync` — validates: all target nodes `Active`, no node already part of
  another non-terminal rolling operation; assigns nodes to waves; writes
  `sync_operation` + one `sync_operation_step` per node.
- `PauseAsync` / `ResumeAsync` / `AbortAsync` — operation-level state changes.
  Abort marks remaining `Pending` steps `Skipped` and restores in-flight nodes
  (clear maintenance flag, `Resume` lifecycle).
- `ConfirmStepAsync` — completes a `manual-confirm` maintenance window.

### RollingOperationWorker (MSOSync.App)

`BackgroundService`, `PeriodicTimer` (~15 s), registered with
`IWorkerStatusRegistry`, records tick start/complete/failed
(RULE-WRK-1/2/3). Stateless tick: re-reads DB and advances the step state
machine, so it is restart-safe by construction and never holds in-memory
operation state.

Step flow per node:

```
Pending → StartDrain → (wait DRAIN_COMPLETED) → set MaintenanceMode
  → maintenance op: manual confirm  OR  auto-window elapses
  → upgrade op:     AwaitingVerification until heartbeat AgentVersion == target
                    (timeout → step Failed)
  → clear MaintenanceMode → Resume → Completed
```

Wave gating: wave N+1 starts only after every wave-N node has been healthy for
the configured soak period (Connected, heartbeat fresh, no config-drift
`Failed`). A gate failure auto-`Pause`s the operation and raises an Epic 13
notification.

## APIs

`NodeLifecycleController` (existing):

- `POST /api/v1/node-lifecycle/{nodeId}/drain`
- `POST /api/v1/node-lifecycle/{nodeId}/resume`

New `RollingOperationsController` — `/api/v1/operations/rolling`:

- `POST /` — create (maintenance or upgrade)
- `GET /{id}` — operation detail with steps
- `POST /{id}/pause`, `POST /{id}/resume`, `POST /{id}/abort`
- `POST /steps/{stepId}/confirm`

Permission: existing `MANAGE_NODE_LIFECYCLE` (no new permission).
All endpoints follow Phase 2A rules: named DTOs, `ProducesResponseType`,
FluentValidation validator for the create request, exceptions via
`GlobalExceptionHandler` (new `OperationStateException` registered per
RULE-ERR-2).

SignalR: reuse `OperationsHub` operations group (Jobs page already subscribes);
step/wave changes publish operation progress updates.

## Debt Absorption — 2A-029

Extract direct `AppDbContext` usage into query services:

- `AuthController` switch-tenant membership lookup
- `BatchController` outgoing-batch queries
- `NodeLifecycleController` node read

Controllers then satisfy RULE-CTL-2 / RULE-ARCH-3.

## Frontend

- `NodeActionsMenu`: Drain / Resume entries (menu is transitions-driven, so this
  follows from the API's available-transitions payload).
- `LifecycleBadge` + topology recolor: `Draining` color; node detail shows drain
  progress (in-flight outgoing batch count).
- Jobs page (12C): "New Rolling Operation" wizard — type, node/group selection,
  wave size (count/%), gate soak duration, wave action, target version
  (upgrade only). Operation detail panel: wave × step grid with live SignalR
  progress, Pause / Resume / Abort / Confirm-wave actions.
- NodesGrid: `AgentVersion` column.

## Testing

Per `docs/architecture/test-infrastructure.md` conventions:

- **Unit (MetadataTests):** `NodeLifecycleStateMachine` Draining transitions;
  `RollingOperationService` create/abort/confirm validation; version-gate and
  wave-gate logic.
- **Unit (AppTests):** `RollingOperationWorker` tick state machine via internal
  tick method + mocks (RULE-TEST-1/2); drain-completion detection.
- **Integration (IntegrationTests):** rolling API endpoints, drain endpoint
  matrix, M033 migration smoke.
- **Frontend (Vitest):** wizard, badges, operation detail panel.

## Out of Scope

- Binary/package delivery for upgrades (operator installs; hub only verifies).
- Replay engine (2B.2) and DR dashboard / health rollup / config diff views (2B.3).
- New permissions, new hubs, new worker frameworks.
