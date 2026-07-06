# Epic 12B-1: Node Lifecycle Engine — Master Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Each task lives in its own file (links below); read ONLY your task file plus this master's Global Constraints.

**Goal:** Replace the six-literal `Status` string model with a canonical `NodeLifecycleState` enum driven by a single mutation gateway (`NodeLifecycleService`), separate connectivity into a telemetry-derived dimension owned by a new `ConnectivityEvaluator`, land the 12A deferred approve→SyncNode item, and ship the complete operator lifecycle UI — as one hard-cutover epic branch.

**Architecture:** Pure `NodeLifecycleStateMachine` (transition table = single canonical authority) + command pipeline in `NodeLifecycleService` (authorize → load → lock → validate → persist state+history+audit in one transaction → commit → publish MediatR after commit). Connectivity: heartbeat endpoint and ProbeWorker write telemetry only; `ConnectivityEvaluator` worker is the sole `ConnectivityStatus` writer via pure `IConnectivityPolicy`. `DecommissionWorker` finalizes drains through the gateway only. Frontend renders lifecycle/connectivity/maintenance as three badges and drives all actions from the backend `/transitions` metadata endpoint.

**Tech Stack:** C# 13 / .NET 9, EF Core 9, MediatR notifications, SignalR; React 19 + TypeScript + TanStack Query v5 + AG Grid + shadcn/Sonner. xUnit 2.9.3, FluentAssertions 6.12.2, SQLite unit tests, Testcontainers/LocalDB integration tests, Vitest.

**Spec:** `docs/superpowers/specs/2026-07-06-epic12b1-node-lifecycle-engine-design.md` (frozen, CTO-approved). Section references (§) in task files point there.

---

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings at every task boundary (`dotnet build MSOSync.sln -c Debug --warnaserror`).
- **No new NuGet or npm packages.**
- Before any `dotnet` command:
  ```pwsh
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
  ```
- Lifecycle enum persisted in existing `status` column via `HasConversion<string>()`; column widened to `varchar(30)`.
- The 11 lifecycle invariants (spec §2.4) are binding; each has a named test.
- Exactly one lifecycle writer (`NodeLifecycleService`), one connectivity writer (`ConnectivityEvaluator`), one transition authority (`NodeLifecycleStateMachine`), one eligibility authority (`INodeSyncPolicy`).
- MediatR notifications published only AFTER commit. Events idempotent.
- Every command generates a `CorrelationId` (Guid) flowing into history, audit detail, notifications, error bodies, and log scopes.
- Lifecycle history immutable append-only; migration seed rows permanent (`FromState=NULL`, `Trigger=Migration`).
- Bootstrap tokens: one-time, BCrypt-hash-stored, returned once, never logged.
- Auth boundaries strict: `/api/v1/nodes/*` node credentials only; `/api/v1/node-lifecycle/*` + `/api/v1/node-management/*` operator JWT only.
- Frontend: zero hardcoded transition rules — action menus driven by `GET /transitions` metadata. No color-only state encoding (color + icon + label).
- Tests: xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72; unit tests SQLite (never EF InMemory); integration tests use `NodeManagementFixture` pattern.
- Git safety: stage files by name only (never `git add .`/`-A`); never commit `.env` variants.
- Hard cutover: every task leaves the build green; no dual lifecycle paths at any commit boundary; epic merges as one branch.

## Deviations from spec discovered during planning

These are implementation-reality corrections, not architectural changes:

1. **Migration is `M022_NodeLifecycle`, not M021** — `M021_AddNodeTypeExternalId` already exists (committed 2026-07-06). All spec references to "M021" mean this M022.
2. **`PROVISION_NODES` is a NEW permission, not a rename** — no `PROVISION` permission exists today; provision endpoints are gated by `MANAGE_USERS`. M022 seeds `PROVISION_NODES` (ADMIN) and `MANAGE_NODE_LIFECYCLE` (OPERATOR + ADMIN); provision endpoints re-gate to `PROVISION_NODES`.
3. **`LastProbeUtc` = existing `LastProbeTime` column** — no duplicate column added; spec's `LastProbeUtc` naming maps to the existing `last_probe_time`.
4. **Bootstrap token storage gap (12A):** `NodeLifecycleService.ProvisionAsync` generates a raw token but never persists a hash — activation cannot validate against nothing. M022 adds `sync_node_bootstrap_token` (hash, expiry, consumed/revoked timestamps); provision + recovery-approve write it; activate validates + consumes it. This satisfies spec §4.5 ("hash match, unused, unexpired, not revoked").
5. **`status` widened `varchar(20)` → `varchar(30)`** (`PendingRegistration` is 19 chars; headroom).
6. **Heartbeat/probe thresholds** come from existing config keys `Heartbeat:IntervalSeconds` (30) and `Heartbeat:ProbeIntervalSeconds` (60) — there is no options class; a new `ConnectivityOptions`/`LifecycleOptions` binding wraps them (spec: "never hardcoded").

---

## Task Files

| # | Task | File | Summary |
|---|------|------|---------|
| 1 | Domain model + M022 + policies | [2026-07-06-epic12b1-task-1-domain-migration.md](2026-07-06-epic12b1-task-1-domain-migration.md) | Enums, entity/EF changes, M022 (convert + drop SyncEnabled + 3 new tables + seeds + permissions), `INodeSyncPolicy`, `IConnectivityPolicy`, mechanical migration of all `Status`-string and `SyncEnabled` readers, delete `NodeStateMachine`/`NodeStatusWorker`, startup validation |
| 2 | State machine + lifecycle service + authorization | [2026-07-06-epic12b1-task-2-state-machine-service.md](2026-07-06-epic12b1-task-2-state-machine-service.md) | `NodeLifecycleStateMachine`, `InvalidLifecycleTransitionException`, command pipeline + all commands (approve→create, activate, enable/disable, maintenance, decommission, recovery), `NodeLifecycleHistoryService`, `NodeAuthorizationService`, credential revocation, delete legacy `ApproveRegistrationAsync` |
| 3 | Connectivity engine + workers | [2026-07-06-epic12b1-task-3-connectivity-workers.md](2026-07-06-epic12b1-task-3-connectivity-workers.md) | `ConnectivityEvaluator` worker (sole status writer + history + prune), ProbeWorker → telemetry-only, heartbeat endpoint lifecycle matrix (403/410), `DecommissionWorker` + `IDecommissionEvaluator` |
| 4 | API + SignalR + audit | [2026-07-06-epic12b1-task-4-api-signalr-audit.md](2026-07-06-epic12b1-task-4-api-signalr-audit.md) | `NodeLifecycleController` (9 endpoints), `POST /nodes/activate`, `NodeStateDto` + transitions metadata, validators, audit constants, `NodeLifecycleChangedEvent`/`NodeMaintenanceChangedEvent` + publisher extension |
| 5 | Frontend foundation | [2026-07-06-epic12b1-task-5-frontend-foundation.md](2026-07-06-epic12b1-task-5-frontend-foundation.md) | `types/lifecycle.ts`, API layer, query keys, `LifecycleBadge`/`ConnectivityBadge`/`MaintenanceBadge`/`NodeStatusSummary`, mutation hooks, SignalR category router extension, Vitest |
| 6 | Lifecycle UI | [2026-07-06-epic12b1-task-6-lifecycle-ui.md](2026-07-06-epic12b1-task-6-lifecycle-ui.md) | Nodes grid 3-badge columns + transitions-driven action menu, maintenance dialog, decommission wizard, node lifecycle panel + history timeline, recovery review, topology recolor, decommissioned filter |
| 7 | Testing + cutover verification | [2026-07-06-epic12b1-task-7-testing-cutover.md](2026-07-06-epic12b1-task-7-testing-cutover.md) | Integration suites (activation, recovery e2e, decommission, heartbeat codes, authz matrix, concurrency, retry safety), post-cutover checklist, full green gate |

Execution order is strict 1→7. Tasks 1–4 backend, 5–6 frontend, 7 verification.

---

## End-to-End Verification (after Task 7)

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.MetadataTests -c Debug --no-build
dotnet test tests/MSOSync.IntegrationTests -c Debug --no-build
cd src/MSOSync.Frontend
npm run test
npm run build
```

Expected: build zero warnings; all tests green; frontend zero TS errors.

Post-cutover checklist (spec §13) verified in Task 7:

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
