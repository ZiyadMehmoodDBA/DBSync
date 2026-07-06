# Epic 12B-1: Node Lifecycle Engine — Design Specification

**Date:** 2026-07-06
**Status:** CTO-approved design
**Backend:** C# 13 / .NET 9 · **Frontend:** React 19 + TanStack Query v5 · **No new packages**

---

## 1. Overview

Epic 12B was split into two bounded contexts:

- **Epic 12B-1: Node Lifecycle Engine** (this spec) — state machine, lifecycle orchestration, operations. Runtime node management.
- **Epic 12B-2: Configuration Management** (future) — templates, versions, assignments, drift, rollback. Builds after 12B-1 matures.

Epic 12A remains the administrative onboarding experience (registration queue, provisioning). 12B-1 owns everything that governs how a node behaves after onboarding, and closes the 12A deferred item: **approve → SyncNode creation/activation**.

### Goals

1. One canonical lifecycle model replacing six inconsistent `Status` string literals.
2. One lifecycle mutation gateway (`NodeLifecycleService`) replacing two conflicting approval paths.
3. Connectivity separated from lifecycle as an independent, telemetry-derived dimension.
4. Complete operator UI: by end of epic, operators drive the full node lifecycle from the frontend without API tools.
5. Hard cutover: no legacy status model, no legacy state machine, no legacy approval path remains anywhere.

### Non-goals (out of scope)

- Configuration management (12B-2).
- Scheduled/rolling maintenance windows, maintenance calendars (future).
- Distributed hub coordination (single-instance hub assumption holds, matching existing workers).
- Cancel-decommission workflow (audit constant reserved; operation not implemented).

---

## 2. Domain Model

### 2.1 NodeLifecycleState

```csharp
public enum NodeLifecycleState
{
    PendingApproval,      // awaiting admin approval (post-cutover reachable only via migrated legacy PENDING rows)
    PendingRegistration,  // SyncNode exists, awaiting /activate handshake
    Active,
    Recovery,             // identity replacement under review / awaiting re-activation
    Disabled,
    Decommissioning,      // orchestrated drain in progress
    Decommissioned,       // terminal
    Rejected              // terminal
}
```

Persisted in the existing `Status` column via `HasConversion<string>()`. Enum order follows the natural lifecycle.

Two further dimensions are **never** part of this enum:

- **ConnectivityStatus** (existing enum: `Unknown`, `Reachable`, `Degraded`, `Unreachable`) — operational health, derived from telemetry only.
- **Maintenance** — operational intent, modeled as orthogonal columns (§3.1).

### 2.2 Transition table (exhaustive)

The state machine encodes exactly this table. Anything not listed is an invalid transition.

| From | To | Trigger |
|---|---|---|
| PendingApproval | PendingRegistration | Approve |
| PendingApproval | Rejected | Reject |
| PendingRegistration | Active | Activate handshake |
| Active | Disabled | Disable |
| Disabled | Active | Enable |
| Active | Recovery | Known ExternalId re-registers |
| Disabled | Recovery | Known ExternalId re-registers |
| Recovery | Active | Recovery approved + node re-activates via /activate |
| Recovery | *PreviousLifecycleState* | Recovery rejected (deterministic) |
| PendingApproval | Decommissioning | Decommission command |
| PendingRegistration | Decommissioning | Decommission command |
| Active | Decommissioning | Decommission command |
| Recovery | Decommissioning | Decommission command |
| Disabled | Decommissioning | Decommission command |
| Decommissioning | Decommissioned | Drain complete or grace timeout |
| Rejected | — | *(no outgoing transitions — terminal)* |
| Decommissioned | — | *(no outgoing transitions — terminal)* |

Notes:

- Recovery **approval** does not transition state. The node stays `Recovery` until its `/activate` handshake succeeds. Approval = trust; activation = proof.
- Decommissioned nodes never enter Recovery. A returning decommissioned identity is a brand-new registration.
- `ExternalId` is freed for reuse only when the node reaches `Decommissioned`, never during `Decommissioning`.
- Maintenance mode is not a transition; it mutates maintenance columns only (§4.3).

### 2.3 LifecycleTrigger

```csharp
public enum LifecycleTrigger
{
    Manual,        // operator command
    Registration,  // registration approval flow
    Activation,    // node /activate handshake
    Recovery,      // recovery flow
    System,        // worker-initiated (e.g., drain finalize on completion)
    Timeout,       // grace-period expiry
    Migration      // M021 conversion
}
```

`Heartbeat` is deliberately absent: lifecycle is never heartbeat-driven.

### 2.4 Lifecycle Invariants

These are binding rules, enforced in code and asserted by tests:

1. `Rejected` and `Decommissioned` are terminal — no outgoing transitions.
2. Only `NodeLifecycleService` may change `LifecycleState`. It is the **only mutation gateway**; no worker, controller, background service, or registration handler mutates lifecycle directly.
3. Only `ConnectivityEvaluator` may change `ConnectivityStatus`.
4. Recovery always records `PreviousLifecycleState` on entry and clears it on exit (both Active and reject paths).
5. Activation is the only transition into `Active` from onboarding states (`PendingRegistration`, `Recovery`). `Enable` is an administrative re-entry for an already trusted node whose identity remains valid.
6. Heartbeat never changes `LifecycleState`.
7. ProbeWorker never changes `LifecycleState`.
8. `LifecycleState` never changes from connectivity observations.
9. `ConnectivityStatus` is derived only from telemetry. It is never mutated by lifecycle transitions, operator actions, or registration events.
10. Lifecycle history is immutable append-only.

### 2.5 NodeLifecycleStateMachine

Pure domain object. No database access, no services, no repositories, no logging.

```csharp
public interface INodeLifecycleStateMachine
{
    bool CanTransition(NodeLifecycleState from, NodeLifecycleState to);
    IReadOnlyList<NodeLifecycleState> AllowedTargets(NodeLifecycleState from);
    void Validate(NodeLifecycleState from, NodeLifecycleState to); // throws InvalidLifecycleTransitionException
}
```

Exhaustively unit-tested: every (from, to) pair asserted allowed or denied.

---

## 3. Schema (Migration M021)

### 3.1 SyncNode changes

**Converted:** `Status` string values → canonical states:

| Legacy | New |
|---|---|
| PENDING | PendingApproval |
| APPROVED | PendingRegistration |
| PROVISIONED | PendingRegistration |
| REGISTERED | Active |
| OFFLINE | Active *(connectivity dimension takes over; evaluator recomputes)* |
| DISABLED | Disabled |

**Dropped:** `SyncEnabled` column. Sync eligibility becomes derived via `INodeSyncPolicy` (§6). All readers migrate.

**Added columns:**

| Column | Type | Purpose |
|---|---|---|
| `PreviousLifecycleState` | nvarchar, nullable | Recovery reject determinism; set on Recovery entry, cleared on exit |
| `MaintenanceMode` | bit, default 0 | orthogonal maintenance flag |
| `MaintenanceReason` | nvarchar, nullable | |
| `MaintenanceStartedAt` | datetimeoffset, nullable | |
| `MaintenanceUntil` | datetimeoffset, nullable | expected end |
| `MaintenanceStartedBy` | nvarchar, nullable | |
| `DecommissionReason` | nvarchar, nullable | |
| `DecommissionStartedAt` | datetimeoffset, nullable | |
| `DecommissionGraceUntil` | datetimeoffset, nullable | drain deadline |
| `DecommissionInitialOpenBatches` | int, nullable | snapshot at decommission start; enables drain progress percent |
| `ConnectivityReason` | nvarchar, nullable | current evaluator reason (diagnostics) |
| `LastProbeUtc` | datetimeoffset, nullable | probe telemetry (add if missing) |
| `LastProbeError` | nvarchar, nullable | probe telemetry (add if missing) |
| `ConsecutiveProbeFailures` | int, default 0 | maintained by ProbeWorker; input to connectivity rule 6 |
| `RowVersion` | rowversion | optimistic concurrency for lifecycle commands |

### 3.2 New table: `sync_node_lifecycle_history`

Authoritative operational timeline. `SyncAudit` remains the compliance log — different concerns, different storage.

| Column | Type |
|---|---|
| `HistoryId` | bigint identity PK |
| `NodeId` | FK → sync_node |
| `FromState` | nvarchar, **nullable** (null = entry into canonical model) |
| `ToState` | nvarchar |
| `Trigger` | nvarchar (LifecycleTrigger, HasConversion) |
| `Reason` | nvarchar, nullable |
| `Actor` | nvarchar (username or "system") |
| `CorrelationId` | uniqueidentifier, nullable |
| `MetadataJson` | nvarchar(max), nullable (richer context without schema churn) |
| `OccurredAt` | datetimeoffset |

Index: `(NodeId, OccurredAt DESC)`.

**Migration seed:** one row per existing node — `FromState = NULL`, `ToState = mapped state`, `Trigger = Migration`, `Reason = "M021 lifecycle model migration"`. The first record means "this node entered the canonical lifecycle model."

### 3.3 New table: `sync_node_connectivity_history`

Rolling connectivity transitions — lifecycle history stays lifecycle-only.

| Column | Type |
|---|---|
| `Id` | bigint identity PK |
| `NodeId` | FK → sync_node |
| `PreviousStatus` | nvarchar |
| `NewStatus` | nvarchar |
| `Reason` | nvarchar |
| `OccurredAt` | datetimeoffset |

Retention: 30 days, configurable; pruned by the ConnectivityEvaluator worker cycle.

### 3.4 Startup validation

Hosted startup check, fail-fast:

- Every `Status` value parses to `NodeLifecycleState` — else fail startup.
- Consistency scan: e.g., `Decommissioned + MaintenanceMode=true` → log error (fail startup for unparseable states; log for soft inconsistencies).
- `ConnectivityStatus` values parse to enum.

---

## 4. Lifecycle Orchestration

### 4.1 NodeLifecycleService — the only mutation gateway

Every public command follows the identical pipeline:

```text
Authorize
  → Load node
  → Acquire lifecycle lock (optimistic: RowVersion + per-node in-process serialization)
  → Validate transition (state machine) — revalidated at execution time, never trusting pre-loaded state
  → Persist state
  → Write lifecycle history
  → Write audit
  → Publish MediatR notification
  → Release lock / return
```

- Each command generates a `CorrelationId` (Guid) at entry; it flows into the history row, audit detail, notification, and error responses.
- Concurrency conflict → existing `ConcurrencyException` → 409 (GlobalExceptionHandler mapping unchanged).
- Invalid transition → new `InvalidLifecycleTransitionException` → 409 (§7.4).
- Concurrent races prevented by the lock: Disable vs Decommission, Recovery-approve vs Disable, Enable vs DecommissionWorker finalize — one wins, one gets 409.

### 4.2 Command surface

| Command | Transition | Trigger | Caller auth |
|---|---|---|---|
| `ApproveAsync` | registration approved → **creates SyncNode** in PendingRegistration | Registration | APPROVE_NODES |
| `RejectAsync` | registration rejected (no SyncNode) / PendingApproval → Rejected | Manual | APPROVE_NODES |
| `ActivateAsync` | PendingRegistration → Active; Recovery → Active | Activation | node bootstrap token |
| `EnableAsync` | Disabled → Active | Manual | MANAGE_NODE_LIFECYCLE |
| `DisableAsync` | Active → Disabled | Manual | MANAGE_NODE_LIFECYCLE |
| `StartMaintenanceAsync` | no transition — maintenance columns + history row (MetadataJson) | Manual | MANAGE_NODE_LIFECYCLE |
| `EndMaintenanceAsync` | no transition — clears maintenance columns + history row | Manual | MANAGE_NODE_LIFECYCLE |
| `DecommissionAsync` | any non-terminal → Decommissioning | Manual | MANAGE_NODE_LIFECYCLE |
| `ForceCompleteDecommissionAsync` | Decommissioning → Decommissioned | Manual | MANAGE_NODE_LIFECYCLE |
| `FinalizeDecommissionAsync` (worker-only) | Decommissioning → Decommissioned | System / Timeout | internal (DecommissionWorker) |
| `EnterRecoveryAsync` (via RegisterAsync) | Active/Disabled → Recovery; stores PreviousLifecycleState | Recovery | anonymous registration endpoint |
| `ApproveRecoveryAsync` | no state change; revokes ALL previous credentials, issues new bootstrap token | Recovery | APPROVE_NODES |
| `RejectRecoveryAsync` | Recovery → PreviousLifecycleState; clears it | Recovery | APPROVE_NODES |

### 4.3 Maintenance semantics

- Settable only on `Active` nodes.
- Enabling: sets `MaintenanceMode=1`, reason (required), `MaintenanceStartedAt`, optional `MaintenanceUntil`, `MaintenanceStartedBy`; best-effort node notification if `notifyNode` requested.
- During maintenance: connectivity evaluated normally (truth preserved); alerts/toasts suppressed at the consumer layer; sync scheduling paused via `INodeSyncPolicy`.
- Changing the window → `NODE_MAINTENANCE_EXTENDED` audit.
- Never a lifecycle state; never appears in the state machine.

### 4.4 Approve / Provision reconciliation (12A collision resolved)

- **Approve** now creates the SyncNode from registration metadata, state `PendingRegistration`. This lands the 12A deferred item.
- **Provision** no longer creates a SyncNode when an approve-created one exists: it generates the package + bootstrap token for a node in `PendingRegistration`. The direct-provision wizard path (no prior registration) still creates the node — also `PendingRegistration`.
- Bootstrap token model unchanged from 12A: one-time, hash-stored, returned once, never logged.
- **Manual admin node creation** (`NodeMetadataService.CreateNodeAsync`, currently writes `PENDING`) now creates nodes in `PendingRegistration` — a manually created node still requires provisioning + activation, but not registration approval (the admin creating it IS the approval). `PendingApproval` is not reachable for new nodes post-cutover.

### 4.5 Activation handshake

`POST /api/v1/nodes/activate` — body: `{ externalId, bootstrapToken, agentVersion }`.

Validations (all at execution time):
- token hash match, unused, unexpired, **not revoked**
- node exists and lifecycle is `PendingRegistration` or `Recovery` (never terminal)
- agent version compatibility
- transition still valid at persist time

On success: token consumed; operational node credential issued (existing NodeSecurity token model); state → `Active`; history `Trigger=Activation`; on Recovery activation additionally `PreviousLifecycleState = null` and audit `NODE_RECOVERY_ACTIVATED`.

Response (200):

```json
{
  "nodeToken": "…",
  "heartbeatIntervalSeconds": 30,
  "probeIntervalSeconds": 60,
  "configurationVersion": 1
}
```

(`configurationVersion` is a fixed `1` until 12B-2 introduces real config versioning.)

Failures: 401 invalid/consumed/revoked token; 409 wrong lifecycle state. Endpoint is anonymous-route (the token is the credential) and rate-limited via existing infrastructure.

### 4.6 Recovery workflow

Recovery = **administrative recovery of an existing node's identity**. Not a health state, not automatic.

```text
Known ExternalId re-registers (node lost / VM rebuilt / credentials lost)
  → EnterRecoveryAsync: lifecycle → Recovery, PreviousLifecycleState stored
  → Admin reviews diff (existing 12A RegistrationDiffService — reused)
  → ApproveRecoveryAsync: revoke previous node auth token + any cached sessions,
       issue new bootstrap token (no state change)
  → Node re-activates via /activate → Active, PreviousLifecycleState cleared
  — or —
  → RejectRecoveryAsync: lifecycle → PreviousLifecycleState (deterministic), cleared
```

- Only one active identity ever exists: credential revocation precedes new credential issuance.
- Decommissioned nodes cannot enter Recovery (state machine forbids).
- Recovery rides the existing registration endpoints: re-registration creates a `RegistrationType=Recovery` request; `registrations/{id}/approve|reject` dispatch internally by type. No parallel recovery API.

### 4.7 Decommission workflow

`Decommissioning` is an **orchestrated drain state**:

1. **Freeze new work** — `INodeSyncPolicy` returns not-eligible (state ≠ Active); no new batches scheduled or routed; new sync sessions rejected.
2. **Drain** — in-flight batches complete or the grace period expires (`gracePeriodMinutes`, request-optional; default 60 minutes from options config).
3. **Revoke trust** — bootstrap + node auth tokens revoked at decommission start; no new activation possible.
4. **Notify node** — best-effort `NODE_DECOMMISSIONING` notification if reachable.
5. **Auto-complete** — `DecommissionWorker` finalizes on drain-complete or grace expiry; no second admin action needed. `ForceCompleteDecommissionAsync` allows manual override.

`Decommissioned` is terminal and immutable: row preserved forever (audit, FKs, history), hidden from default views, `ExternalId` freed for reuse.

`DecommissionReason` (required): free text; UI offers presets (Hardware Replacement, Site Closure, Migration, Duplicate Node, Security Incident, Manual).

**DecommissionWorker + IDecommissionEvaluator:**

```csharp
public interface IDecommissionEvaluator
{
    Task<DecommissionDecision> EvaluateAsync(SyncNode node, CancellationToken ct);
}

public sealed record DecommissionDecision(bool Finalize, DecommissionDecisionReason Reason);
// Reason: DrainCompleted | GraceExpired | OpenBatches
```

Worker polls Decommissioning nodes on the existing worker cadence pattern; evaluator decides (pure, unit-testable); finalization goes through `NodeLifecycleService.FinalizeDecommissionAsync` — no side door. Decision reason flows into history and audit (`NODE_DECOMMISSION_COMPLETED` vs `NODE_DECOMMISSION_FORCED`).

### 4.8 NodeLifecycleHistoryService

Single query/write surface for lifecycle history; controllers never touch the DbSet.

```csharp
public interface INodeLifecycleHistoryService
{
    Task WriteTransitionAsync(LifecycleTransitionRecord record, CancellationToken ct); // called only by NodeLifecycleService
    Task<PagedResult<LifecycleHistoryDto>> GetTimelineAsync(string nodeId, LifecycleHistoryFilter filter, CancellationToken ct);
    Task<LifecycleHistoryDto?> GetLatestAsync(string nodeId, CancellationToken ct);
    Task<NodeStateDto> GetCurrentStateAsync(string nodeId, CancellationToken ct);
}
```

`LifecycleHistoryFilter`: `From`, `To` (date range), `Trigger`, page, pageSize.

---

## 5. Connectivity Engine

### 5.1 Ownership

```text
Heartbeat endpoint   → writes LastHeartbeatUtc (telemetry only)
ProbeWorker          → writes LastProbeUtc, LastProbeError (telemetry only)
ConnectivityEvaluator → SOLE writer of ConnectivityStatus + ConnectivityReason
```

`NodeStatusWorker` is deleted. `ConnectivityEvaluator` is a new hosted worker (hub-only, existing worker options pattern, 30s default cadence). Overlapping cycles prevented: if the previous evaluation is still running, the cycle is skipped.

### 5.2 IConnectivityPolicy (pure)

```csharp
public interface IConnectivityPolicy
{
    ConnectivityEvaluationResult Evaluate(ConnectivityTelemetry snapshot);
}

public sealed record ConnectivityEvaluationResult(ConnectivityStatus Status, ConnectivityReason Reason);
// ConnectivityReason: Healthy | NoHeartbeat | HeartbeatStale | HeartbeatExpired
//                   | ProbeFailed | ProbeFailures | PendingActivation | NotEvaluated
```

Deterministic rules, in order:

```text
1. Lifecycle ∈ {PendingApproval, PendingRegistration, Rejected, Decommissioned} → Unknown / NotEvaluated
2. No heartbeat ever received → Unknown / NoHeartbeat
3. HeartbeatAge > 3 × interval → Unreachable / HeartbeatExpired
4. HeartbeatAge > 1 × interval → Degraded / HeartbeatStale
5. Heartbeat fresh AND last probe failed AND ProbeAge ≤ 2 × probe interval → Degraded / ProbeFailed
6. Heartbeat fresh AND ≥3 consecutive fresh probe failures → Unreachable / ProbeFailures
7. Otherwise → Reachable / Healthy
```

- **Stale probes are ignored** (`ProbeAge > 2 × probe interval`): a just-rebooted healthy node is not downgraded by a pre-reboot probe failure.
- Thresholds come from existing heartbeat/probe options config — never hardcoded.
- On status change: persist, append `sync_node_connectivity_history` row, publish existing `NodeConnectivityChangedEvent(NodeId, Previous, New)`.

### 5.3 Heartbeat endpoint changes

- OFFLINE→REGISTERED auto-flip deleted (no lifecycle writes at all).
- Accepts: `Active`, `Recovery`, `Decommissioning` (draining node still alive — telemetry wanted).
- Rejects: `PendingRegistration` → 403 (activation is the readiness proof — heartbeat before activation would bypass it); `Disabled` → 403; `Decommissioned`/`Rejected` → 410 Gone (agent should stop).
- Maintenance: accepted normally.

### 5.4 ProbeWorker changes

- Stops writing `ConnectivityStatus`; stops publishing connectivity events. Telemetry columns only.
- Probes only nodes with lifecycle ∈ {Active, Recovery, Decommissioning}.
- Maintenance: probing during maintenance is configurable — `MaintenancePolicy.ContinueProbing` (default `true`).

### 5.5 Suppression is a consumer concern

The evaluator owns truth; UI owns presentation. Events always publish; SignalR always broadcasts. The toast/alert layer checks `MaintenanceMode` and suppresses notifications — never data.

---

## 6. Sync Eligibility Policy

`SyncEnabled` column is gone. Single policy service:

```csharp
public interface INodeSyncPolicy
{
    bool CanSynchronize(SyncNode node);          // LifecycleState == Active && !MaintenanceMode
    SyncEligibility Evaluate(SyncNode node);     // Allowed | BlockedByLifecycle | BlockedByMaintenance
                                                 // | BlockedByDecommission | BlockedByPolicy
}
```

Every consumer (sync engine scheduling, topology, PullJob, transport) calls this service — no scattered checks, no computed entity property. `Evaluate` powers future diagnostics; initial callers use `CanSynchronize`.

---

## 7. API Surface

### 7.1 Authentication boundaries (strict)

- `/api/v1/nodes/*` — **node credentials only** (activate: bootstrap token; heartbeat/sync: node token).
- `/api/v1/node-management/*`, `/api/v1/node-lifecycle/*` — **operator JWT only**.
- No endpoint ever accepts both models.

### 7.2 NodeLifecycleController — `/api/v1/node-lifecycle`

| Endpoint | Permission | Returns |
|---|---|---|
| `POST /nodes/{id}/enable` | MANAGE_NODE_LIFECYCLE | 204 |
| `POST /nodes/{id}/disable` | MANAGE_NODE_LIFECYCLE | 204 |
| `POST /nodes/{id}/maintenance/start` `{reason, expectedEndAt?, notifyNode}` | MANAGE_NODE_LIFECYCLE | 204 |
| `POST /nodes/{id}/maintenance/end` | MANAGE_NODE_LIFECYCLE | 204 |
| `POST /nodes/{id}/decommission` `{reason, gracePeriodMinutes?}` | MANAGE_NODE_LIFECYCLE | 202 |
| `POST /nodes/{id}/decommission/force` | MANAGE_NODE_LIFECYCLE | 204 |
| `GET /nodes/{id}/state` | VIEW_TOPOLOGY | 200 `NodeStateDto` |
| `GET /nodes/{id}/transitions` | VIEW_TOPOLOGY | 200 transition metadata |
| `GET /nodes/{id}/history?page&pageSize&from&to&trigger` | VIEW_TOPOLOGY | 200 paged timeline |

FluentValidation on all request DTOs: `reason` required for decommission and maintenance-start.

### 7.3 Canonical projections

**`NodeStateDto`** — the single lifecycle contract consumed by every frontend surface:

```csharp
public sealed record NodeStateDto(
    string NodeId,
    NodeLifecycleState LifecycleState,
    ConnectivityStatus ConnectivityStatus,
    string? ConnectivityReason,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastProbeUtc,
    bool MaintenanceMode,
    string? MaintenanceReason,
    DateTimeOffset? MaintenanceUntil,
    bool DecommissionInProgress,
    int? DrainProgressPercent,       // from DecommissionInitialOpenBatches snapshot; null if initial count 0
    DateTimeOffset? DecommissionGraceUntil);
```

**Allowed-actions preview** — the endpoint returns allowed *actions* (state transitions plus maintenance operations, which are not transitions); frontend never hardcodes rules — backend owns the workflow contract:

```json
{
  "currentState": "Active",
  "allowedTransitions": [
    { "action": "Disable",          "requiresReason": false, "requiresConfirmation": true,  "dangerLevel": "Normal"   },
    { "action": "StartMaintenance", "requiresReason": true,  "requiresConfirmation": false, "dangerLevel": "Normal"   },
    { "action": "Decommission",     "requiresReason": true,  "requiresConfirmation": true,  "dangerLevel": "Critical" }
  ]
}
```

### 7.4 Error model

New `InvalidLifecycleTransitionException` → 409 via GlobalExceptionHandler:

```json
{
  "code": "INVALID_LIFECYCLE_TRANSITION",
  "from": "Disabled",
  "requested": "Recovery",
  "allowedTransitions": ["Active", "Decommissioning"],
  "correlationId": "…"
}
```

Concurrency → existing `ConcurrencyException` → 409. Validation → 400. Not found → 404.

### 7.5 NodeManagementController

Routes unchanged. Recovery rides existing registration endpoints (`RegistrationType=Recovery` dispatch, §4.6). Provision behavior per §4.4.

### 7.6 NodesController (node-facing)

- `POST /activate` per §4.5.
- Heartbeat per §5.3.

### 7.7 API compatibility statement

- Existing `/api/v1/nodes/*` registration/heartbeat routes remain in place (heartbeat status-code behavior changes are part of the lifecycle contract, not route breaks).
- All new lifecycle APIs live under `/api/v1/node-lifecycle`.
- The single frontend is migrated in-epic; no external API consumers exist (CE deployment).

---

## 8. Events & SignalR

New MediatR notifications published from the command pipeline:

- `NodeLifecycleChangedEvent(NodeId, PreviousState, NewState, Trigger, CorrelationId)`
- `NodeMaintenanceChangedEvent(NodeId, Enabled)`
- `NodeConnectivityChangedEvent(NodeId, Previous, New)` — existing, now published only by ConnectivityEvaluator.

`NodeOperationsPublisher` extended → "operators" group. Frontend routes by **event category**:

| Category | Invalidates |
|---|---|
| Lifecycle | nodes grid, node-management overview, topology, lifecycle history, node state |
| Maintenance | same minus history |
| Connectivity | nodes grid, topology, node state |

Rules:

- Events are **idempotent**: duplicate delivery produces no duplicate toasts and no cache corruption (invalidation is naturally idempotent; toasts deduplicate by CorrelationId).
- Toasts only for: Activated, Enabled, Disabled, Maintenance Started/Ended, Decommission Started/Completed, Recovery Approved. Connectivity changes = silent badge updates.
- MaintenanceMode suppresses connectivity toasts client-side (§5.5).

---

## 9. Audit

`SyncAudit` = compliance log ("who did what"). Lifecycle history = operational timeline ("how did this node evolve"). Both written by every command, sharing `CorrelationId`.

Extend `NodeManagementAuditActions`:

```csharp
NODE_ACTIVATED, NODE_ENABLED, NODE_DISABLED,
NODE_MAINTENANCE_STARTED, NODE_MAINTENANCE_EXTENDED, NODE_MAINTENANCE_ENDED,
NODE_DECOMMISSION_STARTED, NODE_DECOMMISSION_COMPLETED, NODE_DECOMMISSION_FORCED,
NODE_DECOMMISSION_CANCELLED,   // reserved; operation not implemented in 12B-1
NODE_RECOVERY_REQUESTED, NODE_RECOVERY_APPROVED, NODE_RECOVERY_REJECTED, NODE_RECOVERY_ACTIVATED
```

Existing 12A constants (NODE_REGISTERED, NODE_APPROVED, NODE_REJECTED, NODE_RE_REGISTERED, PROVISION_PACKAGE_DOWNLOADED) unchanged.

---

## 10. Permissions & Authorization

| Permission | Responsibility |
|---|---|
| `VIEW_TOPOLOGY` | read nodes, lifecycle, history, connectivity |
| `APPROVE_NODES` | registration approval/rejection, recovery approval/rejection (identity trust) |
| `PROVISION_NODES` *(renamed from PROVISION)* | provisioning packages, bootstrap |
| `MANAGE_NODE_LIFECYCLE` *(new)* | enable, disable, maintenance, decommission, force-complete (operational control) |

Migration updates `SystemPermissions` + role seed remap for the rename.

**`NodeAuthorizationService`** — centralizes authorization with two explicit stages:

1. **Permission validation** (policy check).
2. **Business rule validation** (e.g., cannot disable a Decommissioned node, cannot enable a Rejected node, cannot decommission a terminal node).

Controllers stay thin: `[Authorize]` + delegate.

---

## 11. Frontend

### 11.1 Shared foundation — `src/shared/components/node/`

- `LifecycleBadge`, `ConnectivityBadge`, `MaintenanceBadge` — plus a single composite renderer used everywhere:

```tsx
<NodeStatusSummary lifecycle={…} connectivity={…} maintenance={…} />
```

- State is never encoded by color alone — each badge pairs color with an icon/shape and label (accessibility).
- `types/lifecycle.ts` mirrors backend enums exactly: `NodeLifecycleState`, `ConnectivityStatus`, `ConnectivityReason`, `LifecycleTrigger`, `NodeStateDto`, transition metadata, history types.
- API layer `lifecycle.ts` + TanStack Query hooks + mutation hooks with toast-on-settled (existing 10C/10D pattern).

### 11.2 Nodes grid

- Old Status column → three columns: Lifecycle / Connectivity / Maintenance badges.
- Old SyncEnabled-wired enable/disable actions deleted. Action menu driven entirely by `GET /transitions` metadata — `requiresReason` / `requiresConfirmation` / `dangerLevel` decide dialog vs wizard vs confirm; zero hardcoded transition rules in the frontend.
- Decommissioned hidden by default; "Include decommissioned" filter.
- Topology recolors by lifecycle + connectivity ring; shapes/icons accompany color.

### 11.3 Dialogs & wizards

- **Maintenance dialog**: reason (required), expected end (optional), notify-node checkbox.
- **Decommission wizard** (3 steps, 12A wizard pattern): reason presets + grace period → impact preview (open batch count, credential revocation warning) → typed confirmation.
- **Disable/Enable**: existing ConfirmDialog.
- **Force complete**: gated confirm, MANAGE_NODE_LIFECYCLE.

### 11.4 Node detail — lifecycle panel

- `NodeStateDto` card: `NodeStatusSummary` + connectivity reason + heartbeat/probe ages + drain progress bar during Decommissioning.
- **History timeline**: paged, filterable (trigger, date range), grouped by day; each entry shows `FromState → ToState`, trigger, actor, reason; `CorrelationId` in a collapsible detail for cross-referencing audit/logs.

### 11.5 Recovery review

- Registration queue: Recovery rows get a distinct badge + current-node context panel above the existing 12A diff viewer. Same approve/reject endpoints — no new flow.

### 11.6 Permissions

- Existing 11F permission gates: lifecycle actions hidden without MANAGE_NODE_LIFECYCLE; recovery approve hidden without APPROVE_NODES.

---

## 12. Testing

### Unit

- **State machine**: exhaustive (from, to) matrix — every pair asserted allowed/denied; each invariant (§2.4) as a named test.
- **NodeLifecycleService** (SQLite): per command — pipeline completeness (state + history + audit + event all written, shared CorrelationId), concurrency conflict → `ConcurrencyException`, invalid transition → `InvalidLifecycleTransitionException`, Recovery PreviousLifecycleState set/cleared.
- **IConnectivityPolicy**: table-driven — heartbeat ages, probe ages (stale-probe ignore), consecutive failure counts, lifecycle exclusions.
- **IDecommissionEvaluator**: table-driven — open batches / drain complete / grace expired → decision + reason.
- **INodeSyncPolicy**: eligibility matrix.
- **Migration mapping**: REGISTERED→Active, OFFLINE→Active (+connectivity recalculated by evaluator), DISABLED→Disabled, PENDING→PendingApproval, APPROVED/PROVISIONED→PendingRegistration; history seed rows present.

### Integration

- Activation: happy path; consumed-token replay → 401; revoked token → 401; wrong state → 409.
- Recovery end-to-end: re-register → Recovery + PreviousLifecycleState → diff visible → approve (old credentials rejected afterward) → re-activate → Active + cleared.
- Decommission: open batch blocks finalize; grace expiry forces; force-complete endpoint.
- Heartbeat: PendingRegistration → 403; Disabled → 403; Decommissioned → 410; Active accepted.
- Authorization matrix: MANAGE_NODE_LIFECYCLE required on all mutating lifecycle endpoints; VIEW_TOPOLOGY cannot mutate; unauthenticated → 401.
- Concurrency: parallel disable + decommission on same node → exactly one 204/202, one 409.

### Frontend

- Badge components + action menu rendered from transitions payload (Vitest).
- `npm run build` — zero TypeScript errors, zero warnings.

### Gate

- `dotnet build MSOSync.sln -c Debug --warnaserror` clean; full test suite green.

---

## 13. Migration & Cutover

Hard cutover, single epic branch, atomic:

```text
Pre-deployment: backup sync_node (+ new table DDL is additive)
  → M021: status conversion + new columns + drop SyncEnabled + 2 history tables + seed rows
  → Permission seed: MANAGE_NODE_LIFECYCLE + PROVISION→PROVISION_NODES role remap
  → Delete legacy: INodeStateMachine/NodeStateMachine, NodeStatusWorker,
      NodeMetadataService.ApproveRegistrationAsync + endpoint + frontend callers,
      all SyncEnabled readers → INodeSyncPolicy,
      NodeMetadataService.CreateNodeAsync PENDING write → PendingRegistration (§4.4)
  → Startup validation (fail fast)
  → Open traffic
```

Every task leaves the build green; no dual paths at any commit boundary; the epic merges as one branch.

### Post-cutover verification checklist

```text
✓ No legacy Status values remain in sync_node
✓ SyncEnabled removed everywhere (column + all readers)
✓ NodeStateMachine absent
✓ NodeStatusWorker absent
✓ Old ApproveRegistrationAsync + endpoint absent
✓ Startup validation passed
✓ All lifecycle APIs reachable
✓ SignalR lifecycle events flowing
✓ History tables populated (seed rows present)
✓ ConnectivityEvaluator running (statuses updating)
```

---

## 14. Architectural Guarantees

The constitution of this subsystem. Future contributors must never violate these:

```text
Lifecycle state          — exactly one writer: NodeLifecycleService
Connectivity status      — exactly one writer: ConnectivityEvaluator
Lifecycle history        — immutable, append-only
Audit                    — immutable compliance log
Transition rules         — owned exclusively by NodeLifecycleStateMachine
Sync eligibility         — owned exclusively by INodeSyncPolicy
Lifecycle mutations      — only through NodeLifecycleService commands
Node authentication      — established only through the Activate handshake
Connectivity derivation  — telemetry only; never lifecycle, operator, or registration driven
Terminal states          — Rejected and Decommissioned have no exits
```

---

## 15. Definition of Done

```text
✓ Legacy lifecycle removed (INodeStateMachine, NodeStatusWorker)
✓ Legacy status model removed (6 string literals, SyncEnabled)
✓ Legacy approval path removed (NodeMetadataService.ApproveRegistrationAsync)
✓ Lifecycle engine authoritative (all transitions through NodeLifecycleService)
✓ Connectivity engine authoritative (evaluator sole writer)
✓ Lifecycle timeline operational (history table + UI)
✓ Audit operational (all new constants wired)
✓ UI fully migrated (badges, action menus, dialogs, timeline, recovery review)
✓ SignalR integrated (lifecycle + maintenance + connectivity categories)
✓ All tests green (unit + integration + frontend)
✓ Zero compiler warnings (--warnaserror)
✓ No TODO/FIXME left in lifecycle code
✓ Documentation updated (this spec + 12A spec cross-references)
```
