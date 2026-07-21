# Phase 2A Audit Backlog

Findings from the Phase 2A platform stabilization audit. Every finding must reach
an explicit resolution (Fixed / Deferred / Accepted) before Phase 2A exits.

Spec: `docs/superpowers/specs/2026-07-17-phase-2A-platform-stabilization.md`
Master plan: `docs/superpowers/plans/2026-07-17-phase-2A-master.md`

| ID | Finding | Workstream | Priority | Status |
|---|---|---|---|---|
| 2A-001 | ExportJobController returns anonymous `new { jobId }` on 202 | 2A.1 | P2 | Complete (8809041) |
| 2A-002 | PreferencesController manual key validation instead of FluentValidation | 2A.3 | P2 | Complete (f192a8c) |
| 2A-003 | `CreateExportJobRequest`, `ExportJobDto` defined inline in controller file | 2A.6 | P2 | Complete (282145c) |
| 2A-004 | HeartbeatWorker uses `IConfiguration.GetValue("Heartbeat:IntervalSeconds")` | 2A.8 | P1 | Complete (411649e) |
| 2A-005 | ProbeWorker uses `IConfiguration.GetValue("Heartbeat:ProbeIntervalSeconds")` | 2A.8 | P1 | Complete (2a3b43c) |
| 2A-006 | ConnectivityEvaluator uses raw IConfiguration for heartbeat/probe intervals | 2A.8 | P1 | Complete (bb8e14b) |
| 2A-007 | PullJob uses `IConfiguration.GetValue("Sync:PullIntervalSeconds")` | 2A.8 | P1 | Complete (7f4ed50) |
| 2A-008 | SyncJob uses `IConfiguration.GetValue("Sync:IntervalSeconds")` | 2A.8 | P1 | Complete (e27cb14) |
| 2A-009 | SyncJob missing IWorkerStatusRegistry | 2A.9 | P1 | Complete (5a11a48) |
| 2A-010 | PullJob missing IWorkerStatusRegistry | 2A.9 | P1 | Complete (2eeb363) |
| 2A-011 | RetryJob missing IWorkerStatusRegistry, interval hardcoded | 2A.9 | P1 | Complete (91ae119) |
| 2A-012 | PurgeJob missing IWorkerStatusRegistry, uses Task.Delay loop | 2A.9 | P1 | Complete (3aa1d8b) |
| 2A-013 | ConnectivityEvaluator missing IWorkerStatusRegistry (found during 2A.9 verification) | 2A.9 | P1 | Complete |
| 2A-014 | 27 integration tests require Docker (Testcontainers fixtures: Metadata, Transport, Engine, Operations, migration smoke) — fail on machines without Docker | 2A.10 | P2 | Accepted (environment) |
| 2A-015 | MultiTenancy integration tests fail when co-run with other collections: `ApplyTenantFilters` bakes `ICurrentTenantAccessor` into EF's cached model via `Expression.Constant`; default model cache keys on context type only, so the first-built fixture's accessor (or null) wins for all parallel collections. Benign in production (single DI singleton accessor). Fixed with context-instance-member filter: filters always applied, tenant id read via `AppDbContext.CurrentTenantId` so EF rewrites the context reference per instance; lifted nullable comparison avoids eager `.Value` evaluation. | 2A.10 | P2 | Complete |
| 2A-016 | Stale count assertions: permission catalog tests expected 15 (now 16 after M029 MANAGE_PLUGINS), schema test expected 43 tables (now 45 after M028) | 2A.10 | P2 | Complete |
| 2A-017 | AuditController inline range validation (INVALID_RANGE, RANGE_TOO_LARGE) instead of FluentValidation | 2A.1 | P2 | Complete (2cf0d29) |
| 2A-018 | ExportJobController inline filtersJson/resourceType validation with string BadRequest bodies — handle with 2A-001/2A-003 rework | 2A.1 | P2 | Complete (6efd8b8) |
| 2A-019 | OperationsController inline pageSize range check instead of FluentValidation | 2A.1 | P2 | Complete (874a3d7) |
| 2A-020 | BatchController ExportBatches inline status validation (INVALID_STATUS) instead of FluentValidation | 2A.1 | P2 | Complete (421c11a) |
| 2A-021 | Business-rule BadRequests reviewed and acceptable under RULE-VAL-2: NodesController nodeId/token mismatch, SyncController invalid compressed payload, NotificationController unread-unsupported + null-body guard | — | P2 | Accepted |
| 2A-022 | Additional inline controller DTOs found during 2A.6 sweep: `SwitchTenantRequest` (AuthController), `PatchNotificationRequest` (NotificationController), `PluginDto`/`PluginSummaryDto`/`PluginActionResult` (PluginController) | 2A.6 | P2 | Complete (0a2fed4) |
| 2A-023 | OperationsIntegrationTests: 4/8 fail with fixture cleanup error `Cannot open database "MSOSyncOperations_Test" ... Login failed for user 'mcmso\zmehmood'` — pre-existing environmental (reproduced on clean baseline via git stash); same class as 2A-014 | 2A.10 | P2 | Accepted (environment) |
| 2A-024 | Full RULE-API-2/RULE-API-1 sweep beyond 2A-001: 25 anonymous response bodies replaced with named DTOs (shape-preserving, incl. SEQUENCE_GAP wire contract) and 79 unannotated actions across 16 controllers given ProducesResponseType | 2A.1 | P2 | Complete (c51fac8, adbf3c4) |
| 2A-025 | ConflictException not mapped in GlobalExceptionHandler — fell through to 500 catch-all (thrown by ConfigurationTemplateService draft conflict) | 2A.2 | P1 | Complete (c09bc1d) |
| 2A-026 | X-Correlation-Id response header not written by GlobalExceptionHandler (RULE-ERR-3); correlationId was JSON-body-only | 2A.2 | P2 | Complete (c09bc1d) |
| 2A-027 | PluginController enable/disable and UsersController update/delete catch NotFoundException already handled globally (RULE-ERR-1); 404 body changes from empty to error JSON — tests assert status only | 2A.2 | P2 | Complete (c09bc1d) |
| 2A-028 | TenantAccessException bypasses GlobalExceptionHandler by design: caught by TenantResolverMiddleware before MVC pipeline, legacy `{ "error" }` shape (401/403/409) | 2A.2 | P3 | Accepted (documented in exception-hierarchy.md) |
| 2A-029 | AppDbContext injected directly in 3 controllers (AuthController switch-tenant membership lookup, BatchController outgoing-batch queries, NodeLifecycleController node read) — RULE-ARCH-3 | 2A.5 | P2 | Deferred (extract to query services in Phase 2B) |
| 2A-030 | SyncController references MSOSync.Engine (IApplyService) — required by transport apply path for incoming pushed batches; RULE-ARCH-1 narrowed to Scheduler-only | 2A.5 | P3 | Accepted (documented in service-responsibility-map.md) |
