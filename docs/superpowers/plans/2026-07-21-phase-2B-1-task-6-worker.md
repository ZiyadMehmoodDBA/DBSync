# 2B.1 Task 6 — RollingOperationWorker

**Files:**
- Modify: `src/MSOSync.Common/Options/LifecycleOptions.cs` (locate: `grep -rn "DecommissionWorkerIntervalSeconds" src/`) — add `RollingWorkerIntervalSeconds`
- Modify: `src/MSOSync.App/appsettings.json` — add key under existing Lifecycle section
- Create: `src/MSOSync.Scheduler/Workers/RollingOperationWorker.cs`
- Modify: `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj` — no change needed (InternalsVisibleTo for SchedulerTests already present)
- Modify: App host registration (locate: `grep -rn "DecommissionWorker" src/MSOSync.App src/MSOSync.Api` for the `AddHostedService` site) — register `RollingOperationWorker`
- Test: Create `tests/MSOSync.SchedulerTests/RollingOperationWorkerTests.cs`

**Interfaces:**
- Consumes: Task 1 (`SyncOperationStep`, `DrainCompletedAt`, `AgentVersion`), Task 2 (`INodeLifecycleService.StartDrainAsync/ResumeFromDrainAsync/StartMaintenanceAsync/EndMaintenanceAsync`, `NodeManagementAuditActions.NodeDrainCompleted`), Task 4 (`RollingStepStatus`, `RollingOperationPolicy.FromJson`), existing `INodeLifecycleHistoryService` (verify write signature via `grep -rn "INodeLifecycleHistoryService" src/MSOSync.Metadata`), `IWorkerStatusRegistry`, `IClock` (existing abstraction — use it, not `DateTime.UtcNow`).
- Produces: `internal Task RunTickAsync(CancellationToken ct)` (testable per RULE-TEST-1); worker name `nameof(RollingOperationWorker)`.

**Worker responsibilities per tick (all DB-driven, no in-memory state):**
1. **Drain detection (all Draining nodes, incl. standalone):** for each node `LifecycleState == Draining && DrainCompletedAt == null`, if open outgoing batches count == 0 → set `DrainCompletedAt = clock.UtcNow`, write history row `NODE_DRAIN_COMPLETED`.
   Open-batch predicate: copy the exact predicate used by `DecommissionEvaluator`/decommission drain logic (`grep -rn "DecommissionInitialOpenBatches" src/` to find it) — reuse the same status set, do not invent one.
2. **Advance rolling operations** (`Status == "Running"`, types RollingMaintenance/RollingUpgrade):
   - Determine active wave = lowest `WaveNumber` with any non-terminal step (`Pending|Draining|InMaintenance|AwaitingVerification`).
   - Wave gate: if active wave > 1 and any previous-wave node fails health check (healthy = `LifecycleState == NodeLifecycleState.Active && !MaintenanceMode && ConnectivityStatus == ConnectivityStatus.Reachable`) OR previous wave finished less than `GateSoakSeconds` ago (max `CompletedAt` of previous wave) → if unhealthy: set op `Status = "Paused"`, `ProgressMessage = "Health gate failed: <nodeIds>"`, skip op this tick. If merely within soak: skip starting new `Pending` steps, still advance in-flight steps.
   - Per step in active wave:
     - `Pending` → call `StartDrainAsync(nodeId, "Rolling operation", "system")`, set `Status = Draining`, `StartedAt = clock.UtcNow`.
     - `Draining` → if node `DrainCompletedAt != null` → `StartMaintenanceAsync(nodeId, reason: "Rolling operation", until: auto-window ? now+WindowSeconds : null, actor: "system")` (verify exact `StartMaintenanceAsync` parameters and adapt), set `Status = InMaintenance`.
     - `InMaintenance` → auto-window: if `clock.UtcNow >= StartedAt + WindowSeconds` (track window start via node `MaintenanceStartedAt`) → `Status = AwaitingVerification`. manual-confirm: wait (ConfirmStepAsync does the move).
     - `AwaitingVerification` → upgrade op: if node `AgentVersion == policy.TargetVersion` → finish step (below); if `clock.UtcNow - StartedAt > VerificationTimeoutSeconds` → `Status = Failed`, `ErrorMessage = "Verification timeout"`, op `Status = "Paused"`. maintenance op: finish step immediately.
     - Finish step = `EndMaintenanceAsync` (if `MaintenanceMode`), `ResumeFromDrainAsync`, `Status = Completed`, `CompletedAt = clock.UtcNow`.
   - Operation completion: no non-terminal steps left → op `Status = Completed`, `Result = steps.Any(Failed) ? "PartialSuccess" : "Success"`, `CompletedAt`, `ProgressPercent = 100`.
   - Progress: `ProgressPercent = completedSteps * 100 / totalSteps`, `ProgressMessage = $"Wave {activeWave}/{maxWave}"` each tick.

- [ ] **Step 1: Options + appsettings**

`LifecycleOptions`: add `public int RollingWorkerIntervalSeconds { get; set; } = 15;`
`appsettings.json` Lifecycle section: `"RollingWorkerIntervalSeconds": 15`.

- [ ] **Step 2: Failing worker tests**

`tests/MSOSync.SchedulerTests/RollingOperationWorkerTests.cs` — follow `PurgeJobTests`/`SyncJobTests` pattern exactly: real `ServiceCollection`, InMemory `AppDbContext` shared instance registered scoped, mocks for `INodeLifecycleService`, `INodeLifecycleHistoryService`, `IWorkerStatusRegistry`, `IClock` (fixed time). Construct worker with `IServiceScopeFactory` from provider. Tests:

```csharp
[Fact] public async Task RunTick_marks_drain_completed_when_no_open_batches()
// node Draining, DrainCompletedAt null, zero outgoing batches
// → DrainCompletedAt set; history service received NODE_DRAIN_COMPLETED

[Fact] public async Task RunTick_does_not_mark_drain_completed_with_open_batches()
// seed one open outgoing batch for node → DrainCompletedAt stays null

[Fact] public async Task RunTick_starts_first_wave_pending_steps()
// Running RollingMaintenance op, wave1 steps Pending, nodes Active
// → lifecycle.StartDrainAsync called per node; steps Status==Draining, StartedAt set

[Fact] public async Task RunTick_moves_drained_step_to_maintenance()
// step Draining, node DrainCompletedAt set → StartMaintenanceAsync called; step InMaintenance

[Fact] public async Task RunTick_completes_maintenance_step_after_awaiting_verification()
// maintenance op, step AwaitingVerification → EndMaintenance + ResumeFromDrain called; step Completed

[Fact] public async Task RunTick_upgrade_step_waits_for_target_version_then_completes()
// upgrade op TargetVersion "2.0.0": node AgentVersion "1.9" → step stays AwaitingVerification;
// set AgentVersion "2.0.0", tick again → Completed

[Fact] public async Task RunTick_upgrade_verification_timeout_fails_step_and_pauses_op()
// clock beyond StartedAt + VerificationTimeoutSeconds → step Failed, op Status Paused

[Fact] public async Task RunTick_gate_failure_pauses_operation()
// wave1 all Completed, wave1 node ConnectivityStatus Unreachable, wave2 Pending
// → op Paused, wave2 steps remain Pending, no StartDrainAsync for wave2

[Fact] public async Task RunTick_completes_operation_when_all_steps_terminal()
// all steps Completed → op Completed, Result Success, ProgressPercent 100
```

Run: `dotnet test tests/MSOSync.SchedulerTests --filter "FullyQualifiedName~RollingOperationWorkerTests" --nologo`
Expected: FAIL (worker missing).

- [ ] **Step 3: Implement worker**

Skeleton (mirror `DecommissionWorker` verbatim for the timer/registry/Interlocked scaffolding):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MSOSync.Scheduler.Workers;

public sealed class RollingOperationWorker(
    IServiceScopeFactory             scopeFactory,
    IOptions<LifecycleOptions>       lifecycleOptions,
    ILogger<RollingOperationWorker>  logger,
    IWorkerStatusRegistry            registry) : BackgroundService
{
    private int _running;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.RollingWorkerIntervalSeconds > 0
            ? lifecycleOptions.Value.RollingWorkerIntervalSeconds : 15);
        registry.Register(nameof(RollingOperationWorker), interval);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.RollingWorkerIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) continue;
            registry.RecordTickStart(nameof(RollingOperationWorker));
            try
            {
                await RunTickAsync(ct);
                registry.RecordTickComplete(nameof(RollingOperationWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(RollingOperationWorker), ex);
                logger.LogError(ex, "RollingOperationWorker tick failed");
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<INodeLifecycleService>();
        var history   = scope.ServiceProvider.GetRequiredService<INodeLifecycleHistoryService>();
        var clock     = scope.ServiceProvider.GetRequiredService<IClock>();

        await DetectDrainCompletionsAsync(db, history, clock, ct);
        await AdvanceRollingOperationsAsync(db, lifecycle, clock, ct);
    }
    // DetectDrainCompletionsAsync + AdvanceRollingOperationsAsync implement the
    // responsibilities block in this task file, one private method per behavior:
    // ActiveWave, IsWaveHealthy, AdvanceStep, FinishStep, CompleteOperationIfDone.
}
```

Implementation notes:
- Add `using` for namespaces of `AppDbContext`, `INodeLifecycleService`, `IClock`, `IWorkerStatusRegistry`, `LifecycleOptions` (resolve via existing DecommissionWorker usings).
- Open-batch predicate: **copy from DecommissionEvaluator** (same status set).
- All step transitions call `db.SaveChangesAsync(ct)` once at end of the operation loop (single save per op per tick).
- InMemory limitation (RULE-TEST-2 context): no `ExecuteUpdateAsync`/`ExecuteDeleteAsync` — use tracked entities only.
- System actor string: `"system"`.
- `StartMaintenanceAsync`/`EndMaintenanceAsync`: verify parameter lists first and adapt calls; if `StartMaintenanceAsync` requires `until`, pass `clock.UtcNow + WindowSeconds` for auto-window, `null` for manual-confirm.

Register in the same host as DecommissionWorker: `services.AddHostedService<RollingOperationWorker>();`

- [ ] **Step 4: Run tests**

```powershell
dotnet test tests/MSOSync.SchedulerTests --nologo
```

Expected: all green (14 existing + 9 new = 23).

- [ ] **Step 5: Commit**

```powershell
git add src/MSOSync.Scheduler/Workers/RollingOperationWorker.cs src/MSOSync.Common/ src/MSOSync.App/appsettings.json src/MSOSync.Api/ tests/MSOSync.SchedulerTests/RollingOperationWorkerTests.cs
git commit -m "feat(2B.1-T6): RollingOperationWorker — drain detection, wave advance, health gate"
```

(Adjust `git add` paths to wherever the hosted-service registration actually lives.)
