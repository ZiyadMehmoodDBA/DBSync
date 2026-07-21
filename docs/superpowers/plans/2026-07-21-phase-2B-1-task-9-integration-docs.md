# 2B.1 Task 9 — Integration Tests, Docs, Final Gate

**Files:**
- Test: Create `tests/MSOSync.IntegrationTests/Lifecycle/DrainLifecycleTests.cs`
- Test: Create `tests/MSOSync.IntegrationTests/Lifecycle/M033MigrationTests.cs`
- Test: Create `tests/MSOSync.IntegrationTests/Operations/RollingOperationsApiTests.cs` (folder exists — OperationsIntegrationTests.cs lives there)
- Modify: `docs/architecture/audit-backlog-2A.md` (2A-029 → Complete)
- Modify: `docs/architecture/test-infrastructure.md` (counts)
- Modify: `docs/architecture/service-responsibility-map.md` (new services)
- Modify: `docs/architecture/background-workers.md` (RollingOperationWorker row)

**Interfaces:**
- Consumes: everything Tasks 1–8 produced. No new production code in this task — tests + docs only.

- [ ] **Step 1: Drain endpoint integration tests**

`tests/MSOSync.IntegrationTests/Lifecycle/DrainLifecycleTests.cs` — collection/fixture: copy the class header, collection attribute, and authenticated-client helper from `DecommissionTests.cs` in the same folder (same `LifecycleFixture` usage, same node-seeding helper). Tests:

```csharp
[Fact] public async Task Drain_active_node_returns_204_and_state_becomes_Draining()
// seed Active node → POST /api/v1/node-lifecycle/nodes/{id}/drain {"reason":"maintenance window"}
// → 204; GET .../state → lifecycleState "Draining"; history contains NODE_DRAIN_STARTED

[Fact] public async Task Resume_drain_returns_204_and_state_becomes_Active()
// seed node, drain it, then POST .../resume-drain {"reason":null}
// → 204; state Active; history contains NODE_DRAIN_RESUMED

[Fact] public async Task Drain_disabled_node_returns_409()
// seed Disabled node → POST drain → 409 (InvalidLifecycleTransitionException mapping)

[Fact] public async Task Drain_unknown_node_returns_404()

[Fact] public async Task Drain_without_manage_permission_returns_403()
// use the viewer-role client the fixture already provides (see LifecycleAuthorizationTests for the pattern)

[Fact] public async Task Transitions_for_draining_node_include_ResumeDrain_and_Decommission()
// drain node → GET .../transitions → allowedTransitions contains actions "ResumeDrain" and "Decommission"
```

- [ ] **Step 2: M033 migration smoke**

`tests/MSOSync.IntegrationTests/Lifecycle/M033MigrationTests.cs` — copy `M022MigrationTests.cs` structure verbatim (same fixture, same table/column assertion helpers), asserting:

- `sync_node` has columns `agent_version` (nvarchar(100), nullable) and `drain_completed_at` (datetimeoffset, nullable)
- table `msosync.sync_operation_step` exists with columns `step_id`, `operation_id`, `node_id`, `wave_number`, `status`, `started_at`, `completed_at`, `error_message`, `tenant_id`
- indexes `ix_sync_operation_step_op_wave` and `ix_sync_operation_step_tenant_node` exist

- [ ] **Step 3: Rolling API integration tests**

`tests/MSOSync.IntegrationTests/Operations/RollingOperationsApiTests.cs` — fixture: reuse the fixture `OperationsIntegrationTests.cs` uses (note: 2A-023 marks 4 of those tests environmentally failing on DB login — if this fixture cannot connect in this environment, the new tests will fail the same way; that is accepted, record it in the report). Tests:

```csharp
[Fact] public async Task Create_rolling_maintenance_returns_201_with_operation_id()
// seed 2 Active nodes → POST /api/v1/operations/rolling
// { kind:"RollingMaintenance", nodeIds:[a,b], waveSize:1, gateSoakSeconds:0,
//   waveAction:"auto-window", windowSeconds:60, verificationTimeoutSeconds:30 }
// → 201, body.operationId != empty; Location header points at GET route

[Fact] public async Task Get_rolling_operation_returns_policy_and_steps()
// create as above → GET /api/v1/operations/rolling/{id}
// → 200; steps.Count == 2; waves 1 and 2 (waveSize 1); policy round-trips

[Fact] public async Task Create_with_invalid_kind_returns_400()
// kind "Nonsense" → 400 validation problem

[Fact] public async Task Create_with_non_active_node_returns_409_or_400()
// seed Disabled node in nodeIds → assert the Task 4 validation surface
// (OperationStateException → 409; adjust to actual mapping)

[Fact] public async Task Pause_pending_operation_returns_409()
// newly created op has Status Pending (worker not run in test host) → POST pause → 409

[Fact] public async Task Abort_operation_returns_204_and_status_cancelled()
// POST abort → 204; GET → status "Cancelled", pending steps "Skipped"

[Fact] public async Task Rolling_endpoints_without_permission_return_403()
```

Check whether the integration host runs hosted services; if `RollingOperationWorker` runs in the test host and advances ops mid-test, pin these tests to statuses that are stable (Pending before any tick, Cancelled after abort) or disable the worker via `LifecycleOptions.RollingWorkerIntervalSeconds` in the test appsettings — look at how existing tests handle `DecommissionWorker` first.

- [ ] **Step 4: Run integration suite**

```powershell
dotnet test tests/MSOSync.IntegrationTests --nologo
```

Expected: previous baseline (332 passed / 27 accepted environmental failures from 2A-014 Docker + 2A-023 DB login) plus the new tests green — unless the new Operations tests hit the same 2A-023 environment, in which case record them alongside the accepted set with evidence (same connection-string failure signature), not silently.

- [ ] **Step 5: Docs**

`docs/architecture/audit-backlog-2A.md`: 2A-029 row → Complete, note "Phase 2B.1 Task 2 (NodeLifecycleController) + Task 7 (AuthController, BatchController); controllers no longer inject AppDbContext."

`docs/architecture/test-infrastructure.md`: update per-assembly counts from the Step 4 + Task 6/7 runs (SchedulerTests 23; MetadataTests/SecurityTests/IntegrationTests to actual observed numbers — read the run output, don't guess).

`docs/architecture/service-responsibility-map.md`: add rows for `INodeReadQueryService`, `IRollingOperationService`, `IRollingOperationQueryService`, `ITenantMembershipQueryService`, `IOutgoingBatchQueryService`, `RollingOperationWorker` following the file's existing table format.

`docs/architecture/background-workers.md`: add `RollingOperationWorker` entry (interval `Lifecycle:RollingWorkerIntervalSeconds` default 15s, registry name `RollingOperationWorker`, responsibilities: drain-completion detection for all Draining nodes + rolling wave advancement/health gate).

- [ ] **Step 6: Full-solution final gate**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
dotnet test D:\MSOSync\MSOSync.sln --nologo
```

Expected: 0 warnings; every assembly prints `Passed!` except MSOSync.IntegrationTests' accepted environmental failures (verify per-assembly `Passed!`/`Failed!` lines — do not trust the aggregate exit code alone). Frontend already verified in Task 8 Step 12.

- [ ] **Step 7: Commit + master plan checkboxes**

```powershell
git add tests/MSOSync.IntegrationTests/Lifecycle/DrainLifecycleTests.cs tests/MSOSync.IntegrationTests/Lifecycle/M033MigrationTests.cs tests/MSOSync.IntegrationTests/Operations/RollingOperationsApiTests.cs docs/architecture/audit-backlog-2A.md docs/architecture/test-infrastructure.md docs/architecture/service-responsibility-map.md docs/architecture/background-workers.md
git commit -m "test(2B.1-T9): drain + rolling integration tests, M033 smoke; docs updated, 2A-029 closed"
```

Then tick all task checkboxes in `docs/superpowers/plans/2026-07-21-phase-2B-1-master.md`, commit:

```powershell
git add docs/superpowers/plans/2026-07-21-phase-2B-1-master.md
git commit -m "docs(2B.1): plan complete"
```
