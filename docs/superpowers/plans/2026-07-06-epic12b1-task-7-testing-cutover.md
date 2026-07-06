# Epic 12B-1 Task 7: Integration Testing + Migration Validation + Cutover Verification

> Task 7 of 7 — final task. Master plan: `2026-07-06-epic12b1-node-lifecycle-engine.md`. Spec §12, §13. Global Constraints apply. Tasks 1–6 must be complete and green.

**Goal:** Prove the lifecycle contract over HTTP (activation, recovery e2e, decommission drain, heartbeat matrix, authorization, concurrency, retry safety), validate the M022 legacy conversion against a real database, and verify the post-cutover checklist — full solution + frontend green.

**Files:**
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/LifecycleFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/ActivationTests.cs`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/RecoveryEndToEndTests.cs`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/DecommissionTests.cs`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/HeartbeatLifecycleTests.cs`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/LifecycleAuthorizationTests.cs`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/LifecycleConcurrencyTests.cs`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/M022MigrationTests.cs`
- Modify: none in `src/` unless a defect is found (fix defects in the owning file; document in the task report)

**Interfaces:**
- Consumes: everything Tasks 1–6 produced; `NodeManagementFixture` pattern (`tests/MSOSync.IntegrationTests/NodeManagement/NodeManagementFixture.cs`) — copy its LocalDB + login + permission-seed machinery.
- Produces: the epic's verification gate.

---

## Steps

- [ ] **Step 1: LifecycleFixture**

Clone the `NodeManagementFixture` structure into `LifecycleFixture` (own DB name `MSOSyncLifecycle_Test`, own `[CollectionDefinition("Lifecycle")]`). Additions beyond the 12A fixture:

```csharp
// Extra member: an OPERATOR user gains MANAGE_NODE_LIFECYCLE via GrantIfMissingAsync
// (M022 seeds it for OPERATOR, but the fixture seeds permissions explicitly like 12A did).
public async Task<HttpClient> LifecycleManagerClientAsync()   // OPERATOR w/ MANAGE_NODE_LIFECYCLE

// Node seeding helpers (direct AppDbContext access, same scope pattern the 12A fixture uses):
public async Task<string> SeedNodeAsync(NodeLifecycleState state, string externalId, Action<SyncNode>? mutate = null)
// creates a SyncNode in the given state, returns NodeId

public async Task<string> IssueBootstrapTokenAsync(string nodeId)
// resolves IBootstrapTokenService from a service scope, IssueAsync(nodeId, "test"), SaveChanges, returns raw token

public async Task<string> IssueNodeTokenAsync(string nodeId)
// resolves NodeSecurityService, PrepareToken(nodeId), SaveChanges, returns raw token (for heartbeat auth)

public HttpClient NodeClient(string nodeToken)
// client with the node-token auth header exactly as the existing heartbeat tests/middleware expect
// (inspect NodeTokenAuthMiddleware / existing node-auth tests for the header shape)

public async Task RevokeBootstrapTokensAsync(string nodeId)
// scope → IBootstrapTokenService.RevokeAllAsync(nodeId) + SaveChanges

public async Task<JsonElement> GetNodeStateViaApiAsync(string nodeId)
// viewer client → GET api/v1/node-lifecycle/nodes/{nodeId}/state → ReadFromJsonAsync<JsonElement>
```

- [ ] **Step 2: ActivationTests**

```csharp
// tests/MSOSync.IntegrationTests/Lifecycle/ActivationTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Lifecycle")]
public sealed class ActivationTests(LifecycleFixture fixture)
{
    private static object Body(string externalId, string token) =>
        new { externalId, bootstrapToken = token, agentVersion = "1.0.0" };

    [Fact]
    public async Task Activate_PendingRegistration_Returns200_TokenAndIntervals_NodeBecomesActive()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.PendingRegistration, "act-happy");
        var token = await fixture.IssueBootstrapTokenAsync(nodeId);

        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/nodes/activate", Body("act-happy", token));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("nodeToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("heartbeatIntervalSeconds").GetInt32().Should().Be(30);
        body.GetProperty("probeIntervalSeconds").GetInt32().Should().Be(60);
        body.GetProperty("configurationVersion").GetInt32().Should().Be(1);

        var state = await fixture.GetNodeStateViaApiAsync(nodeId);   // helper: viewer client GET /node-lifecycle/nodes/{id}/state
        state.GetProperty("lifecycleState").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Activate_ConsumedTokenReplay_Returns401()   // retry safety
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.PendingRegistration, "act-replay");
        var token = await fixture.IssueBootstrapTokenAsync(nodeId);
        var anon = fixture.AnonymousClient();

        (await anon.PostAsJsonAsync("api/v1/nodes/activate", Body("act-replay", token)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.PostAsJsonAsync("api/v1/nodes/activate", Body("act-replay", token)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activate_RevokedToken_Returns401()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.PendingRegistration, "act-revoked");
        var token = await fixture.IssueBootstrapTokenAsync(nodeId);
        await fixture.RevokeBootstrapTokensAsync(nodeId);   // helper: IBootstrapTokenService.RevokeAllAsync + SaveChanges

        (await fixture.AnonymousClient().PostAsJsonAsync("api/v1/nodes/activate", Body("act-revoked", token)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activate_WrongState_Disabled_Returns409()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, "act-wrongstate");
        var token = await fixture.IssueBootstrapTokenAsync(nodeId);

        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/nodes/activate", Body("act-wrongstate", token));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_LIFECYCLE_TRANSITION");
        body.GetProperty("correlationId").Should().NotBeNull();
    }

    [Fact]
    public async Task Activate_UnknownExternalId_Returns401_NotFoundNotLeaked()
        => (await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/nodes/activate", Body("no-such-node", "whatever")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
```

- [ ] **Step 3: RecoveryEndToEndTests**

One long-flow test plus reject-path test (own fresh ExternalIds; go through the real endpoints):

```text
Recovery_FullFlow:
  1. Seed Active node (externalId "rec-e2e") + operational node token (IssueNodeTokenAsync)
  2. POST api/v1/node-management/registrations {externalId:"rec-e2e", nodeName, nodeType} → 202
  3. Node state (GET /node-lifecycle/.../state) → Recovery; DB check: PreviousLifecycleState == Active
  4. GET api/v1/node-management/registrations/{id} → registrationType "Recovery", diff present
  5. Approve (approver client) → 200; response bootstrapToken non-null
  6. OLD node token rejected: heartbeat with pre-recovery token → 401 (credentials revoked)
  7. POST api/v1/nodes/activate with new bootstrap token → 200
  8. State → Active; PreviousLifecycleState null (DB check)
  9. History (GET .../history) contains Recovery entry + Activation entry sharing the flow's audit trail

Recovery_Reject_ReturnsToPreviousState:
  1. Seed Disabled node "rec-reject"; re-register → Recovery (PreviousLifecycleState == Disabled)
  2. Reject via registrations/{id}/reject → 2xx
  3. State → Disabled; PreviousLifecycleState null

Recovery_DecommissionedExternalId_IsNewRegistration:
  1. Seed Decommissioned node whose ExternalId was freed (ExternalId == "")
  2. Register with the old ExternalId string → creates RegistrationType New (not Recovery)
```

Write these with the same `HttpClient`/`ReadFromJsonAsync<JsonElement>` mechanics as Step 2 — every assertion explicit, no helper hand-waving beyond the fixture members defined in Step 1.

- [ ] **Step 4: DecommissionTests**

```text
Decommission_Returns202_SetsDrainFields_RevokesCredentials:
  seed Active + node token → POST /node-lifecycle/nodes/{id}/decommission {reason:"Site Closure", gracePeriodMinutes:60}
  → 202; state: lifecycleState Decommissioning, decommissionInProgress true, graceUntil ≈ now+60m
  → heartbeat with old node token → 401 (NodeSecurities row removed)
     NOTE: if heartbeat auth middleware returns 401 before lifecycle checks, this asserts revocation, which is the point.

Decommission_OpenBatch_BlocksWorkerFinalize:
  seed Active + one OPEN SyncOutgoingBatch row for the node (status = a non-terminal BatchStatus)
  → decommission with gracePeriodMinutes 60 → resolve IDecommissionEvaluator from a scope:
     EvaluateAsync(node) → Finalize false, Reason OpenBatches
  (worker path unit-tested via evaluator; do not sleep-wait for the hosted worker in integration tests)

Decommission_GraceExpired_EvaluatorFinalizes:
  same but set DecommissionGraceUntil to the past via direct DB update
  → EvaluateAsync → Finalize true, Reason GraceExpired
  → call INodeLifecycleService.FinalizeDecommissionAsync(nodeId, Timeout, "GraceExpired") from a scope
  → state Decommissioned; ExternalId freed (DB check: ExternalId == "")

ForceComplete_Endpoint_Returns204_Terminal:
  seed Decommissioning → POST .../decommission/force → 204 → state Decommissioned
  → any further action (POST .../enable) → 409

Decommission_History_RecordsStartAndComplete_WithReasons
```

- [ ] **Step 5: HeartbeatLifecycleTests**

For each row: seed node in state + issue node token + `PUT/POST` the heartbeat route exactly as existing heartbeat tests do (`POST api/v1/nodes/{nodeId}/heartbeat` with node-token auth + matching body):

```text
Heartbeat_Active_Returns204
Heartbeat_Recovery_Returns204
Heartbeat_Decommissioning_Returns204          // token revoked at decommission start would 401 —
                                              // for THIS test seed Decommissioning directly and issue a fresh token
Heartbeat_PendingRegistration_Returns403
Heartbeat_Disabled_Returns403
Heartbeat_Decommissioned_Returns410
Heartbeat_Rejected_Returns410
Heartbeat_NeverWritesLifecycle                // heartbeat on Active node → state unchanged, no history row added
```

- [ ] **Step 6: LifecycleAuthorizationTests**

Matrix over the Task 4 endpoints (pattern: 12A `AuthorizationTests`):

```text
AllMutatingEndpoints_Unauthenticated_Return401
  (enable, disable, maintenance/start, maintenance/end, decommission, decommission/force)
AllMutatingEndpoints_ViewerRole_Return403
GetState_GetTransitions_GetHistory_ViewerRole_Return200
MutatingEndpoints_LifecycleManagerRole_Succeed      // one representative: enable on Disabled node → 204
Approver_WithoutManageNodeLifecycle_CannotDecommission_403
Provision_WithoutProvisionNodes_Returns403          // MANAGE_USERS alone no longer suffices
```

- [ ] **Step 7: LifecycleConcurrencyTests + retry safety**

```csharp
[Fact]
public async Task ParallelDisableAndDecommission_ExactlyOneWins()
{
    var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "conc-1");
    var c1 = await fixture.LifecycleManagerClientAsync();
    var c2 = await fixture.LifecycleManagerClientAsync();

    var t1 = c1.PostAsJsonAsync($"api/v1/node-lifecycle/nodes/{nodeId}/disable", new { reason = "r" });
    var t2 = c2.PostAsJsonAsync($"api/v1/node-lifecycle/nodes/{nodeId}/decommission",
        new { reason = "race", gracePeriodMinutes = 60 });

    var results = await Task.WhenAll(t1, t2);
    var codes = results.Select(r => (int)r.StatusCode).OrderBy(x => x).ToList();

    // one success (204 disable or 202 decommission), one 409
    codes.Should().HaveCount(2);
    codes.Should().Contain(409);
    codes.Should().Contain(c => c is 202 or 204);
}
```

```text
DuplicateEnable_SecondReturns409                    // Disabled → enable 204 → enable again 409 (already Active)
DuplicateMaintenanceStart_SecondSucceedsAsExtend    // 204 then 204; audit check optional — behavior per spec §12: "409 or idempotent no-op — asserted explicitly"; our design treats repeat as window-extend (Task 2)
DuplicateEndMaintenance_NoOps_204
```

- [ ] **Step 8: M022MigrationTests (legacy conversion on real SQL)**

```csharp
// tests/MSOSync.IntegrationTests/Lifecycle/M022MigrationTests.cs
// Own LocalDB database (MSOSyncM022_Test), NOT the shared fixture:
//   1. Create AppDbContext against the fresh DB
//   2. Migrate to the migration BEFORE M022:
//        var migrator = db.GetService<IMigrator>();
//        await migrator.MigrateAsync("M021_AddNodeTypeExternalId");
//   3. Insert legacy rows via raw SQL (one per legacy status):
//        INSERT INTO msosync.sync_node (node_id, group_id, sync_url, status, sync_enabled, node_type, ...)
//        VALUES ('leg-pending','g','http://x','PENDING',1,'source',...), ('leg-approved',... 'APPROVED'...),
//               ('leg-provisioned'...'PROVISIONED'...), ('leg-registered'...'REGISTERED'...),
//               ('leg-offline'...'OFFLINE'...), ('leg-disabled'...'DISABLED'...);
//   4. await migrator.MigrateAsync();   // applies M022
//   5. Assert via EF (enum materialization doubles as parse validation):
```

```text
Pending_MapsTo_PendingApproval
Approved_And_Provisioned_MapTo_PendingRegistration
Registered_And_Offline_MapTo_Active
Disabled_MapsTo_Disabled
EveryNode_HasSeedHistoryRow_FromStateNull_TriggerMigration_ReasonM022
SyncEnabledColumn_Gone            // raw SQL: SELECT COL_LENGTH('msosync.sync_node','sync_enabled') IS NULL
Permissions_Seeded                // PROVISION_NODES + MANAGE_NODE_LIFECYCLE rows exist; OPERATOR has MANAGE_NODE_LIFECYCLE
```

(Use `Microsoft.EntityFrameworkCore.Infrastructure` for `GetService<IMigrator>`. Drop the DB in `IAsyncLifetime.DisposeAsync` like other fixtures.)

- [ ] **Step 9: Post-cutover checklist verification (spec §13)**

Pre-deployment reminder for the eventual production rollout (goes in the task report, not code): spec §13 mandates a `sync_node` backup before M022 runs — the Down migration is lossy by design. Local/dev runs need no backup.

Source greps — each must return ZERO hits in `src/` (use the Grep tool; migration Designer/snapshot files and this plan/spec are exempt):

```text
"NodeStateMachine"            → absent (class + interface deleted)
"NodeStatusWorker"            → absent
"ApproveRegistrationAsync"    → absent
"SyncEnabled"                 → absent from src/ (except M022 Down() and older migration snapshots)
"\"REGISTERED\"|\"PROVISIONED\"|\"OFFLINE\""  → absent from src/ except migrations
```

Runtime checks (run the app locally against the dev DB — `dotnet run` on the host project):

```text
✓ Startup validation passes (log line "Lifecycle startup validation passed")
✓ GET /api/v1/node-lifecycle/nodes/{id}/state reachable
✓ ConnectivityEvaluator logs cycles; statuses update
✓ SignalR lifecycle event received in the UI on a disable/enable round-trip
✓ History timeline shows M022 seed rows
```

- [ ] **Step 10: Full gate**

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

Expected: zero warnings; all backend + frontend suites green (pre-existing Epic 6 TransportTests CS7036 exclusion unchanged).

- [ ] **Step 11: Commit**

```pwsh
git add tests/MSOSync.IntegrationTests/Lifecycle
git commit -m "test(12B-1): lifecycle integration suites — activation, recovery e2e, decommission drain, heartbeat matrix, authz, concurrency, M022 conversion"
```

After this commit: dispatch the final whole-branch review per superpowers:requesting-code-review, then superpowers:finishing-a-development-branch.
