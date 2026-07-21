# 2B.1 Task 4 — Rolling Operation Service

**Files:**
- Create: `src/MSOSync.Common/Exceptions/OperationStateException.cs` (next to `ConflictException` — verify namespace via existing file)
- Create: `src/MSOSync.Metadata/Operations/Rolling/RollingStepStatus.cs`
- Create: `src/MSOSync.Metadata/Operations/Rolling/RollingOperationPolicy.cs`
- Create: `src/MSOSync.Metadata/Operations/Rolling/IRollingOperationService.cs`
- Create: `src/MSOSync.Metadata/Operations/Rolling/RollingOperationService.cs`
- Modify: `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`
- Modify: Metadata DI extension (same file as Task 2 registration)
- Test: Create `tests/MSOSync.MetadataTests/Operations/RollingOperationServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`SyncOperationStep`, `AppDbContext.OperationSteps`, `OperationType.RollingMaintenance/RollingUpgrade`), existing `IOperationService.CreateAsync(...)` (exact signature in research: type, referenceId, initiatedBy, source, correlationId, canCancel, canRetry, summary, metadataJson, ct), Task 2 (`INodeLifecycleService.StartDrainAsync/ResumeFromDrainAsync`).
- Produces (Tasks 5/6 depend on EXACT signatures):

```csharp
public enum RollingStepStatus { Pending, Draining, InMaintenance, AwaitingVerification, Completed, Failed, Skipped }

public sealed record RollingOperationPolicy(
    int?    WaveSize,
    int?    WavePercent,
    int     GateSoakSeconds,
    string  WaveAction,                    // "manual-confirm" | "auto-window"
    int?    WindowSeconds,
    string? TargetVersion,
    int     VerificationTimeoutSeconds);

public interface IRollingOperationService
{
    Task<Guid> CreateAsync(OperationType kind, IReadOnlyList<string> nodeIds,
        RollingOperationPolicy policy, Guid? initiatedBy, string actor, CancellationToken ct = default);
    Task PauseAsync(Guid operationId, CancellationToken ct = default);
    Task ResumeAsync(Guid operationId, CancellationToken ct = default);
    Task AbortAsync(Guid operationId, string actor, CancellationToken ct = default);
    Task ConfirmStepAsync(Guid stepId, CancellationToken ct = default);
}
```

- `OperationStateException(string message)` with `Code = "OPERATION_STATE_INVALID"` → 409.
- Policy serialization: `System.Text.Json` camelCase into `SyncOperation.MetadataJson`; static helpers `RollingOperationPolicy.ToJson(policy)` / `FromJson(string)`.

- [ ] **Step 1: `OperationStateException`**

Mirror `ConflictException` shape (open it — repo exceptions carry `Code`):

```csharp
namespace MSOSync.Common.Exceptions;

public sealed class OperationStateException(string message)
    : SyncException(message, "OPERATION_STATE_INVALID");
```

Adjust base-ctor call to actual `SyncException` signature (read the file; if base takes `(message)` and `Code` is abstract/property, follow the pattern `ConflictException` uses exactly).

Register in `GlobalExceptionHandler` switch (before catch-all):

```csharp
OperationStateException ex => (409, "Conflict", ex.Code, ex.Message),
```

- [ ] **Step 2: Enum + policy record**

`RollingStepStatus.cs` and `RollingOperationPolicy.cs` as in Interfaces above, plus:

```csharp
public static string ToJson(RollingOperationPolicy policy)
    => System.Text.Json.JsonSerializer.Serialize(policy, JsonOpts);
public static RollingOperationPolicy FromJson(string json)
    => System.Text.Json.JsonSerializer.Deserialize<RollingOperationPolicy>(json, JsonOpts)
       ?? throw new InvalidOperationException("Invalid rolling policy json");
private static readonly System.Text.Json.JsonSerializerOptions JsonOpts =
    new(System.Text.Json.JsonSerializerDefaults.Web);
```

- [ ] **Step 3: Failing service tests**

`RollingOperationServiceTests.cs` — InMemory `AppDbContext` per test (RULE-TEST-2; see `RetryJobTests` pattern for `UseInMemoryDatabase(Guid.NewGuid().ToString())`), mock `IOperationService` (returns fixed Guid, capture metadataJson), mock `INodeLifecycleService`. Seed helper `Node(string id, NodeLifecycleState state)`.

Tests (write all, run, expect FAIL — service missing):

```csharp
[Fact] public async Task Create_assigns_waves_by_wave_size()
// 5 nodes, WaveSize 2 → waves 1,1,2,2,3; 5 step rows Pending; IOperationService.CreateAsync called
// with OperationType.RollingMaintenance and metadataJson == policy json

[Fact] public async Task Create_throws_when_node_not_active()
// one node Disabled → OperationStateException, no rows written

[Fact] public async Task Create_throws_when_node_in_other_running_rolling_op()
// existing sync_operation Status=Running type RollingMaintenance + step for node-1 (non-terminal status)
// → OperationStateException

[Fact] public async Task Create_upgrade_requires_target_version()
// OperationType.RollingUpgrade + policy.TargetVersion null → OperationStateException

[Fact] public async Task Abort_skips_pending_and_restores_inflight()
// steps: node-1 Completed, node-2 Draining, node-3 Pending; abort →
// node-3 Skipped; node-2 -> lifecycle.ResumeFromDrainAsync called; operation Status=Cancelled

[Fact] public async Task Pause_only_from_running() // Pending op → OperationStateException
[Fact] public async Task ConfirmStep_only_when_in_maintenance() // step Pending → OperationStateException
```

Run: `dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~RollingOperationServiceTests" --nologo`
Expected: FAIL (types missing).

- [ ] **Step 4: Implement `RollingOperationService`**

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Rolling;

public sealed class RollingOperationService(
    AppDbContext          db,
    IOperationService     operations,
    INodeLifecycleService lifecycle) : IRollingOperationService
{
    private static readonly string[] NonTerminalStepStatuses =
        [nameof(RollingStepStatus.Pending), nameof(RollingStepStatus.Draining),
         nameof(RollingStepStatus.InMaintenance), nameof(RollingStepStatus.AwaitingVerification)];

    public async Task<Guid> CreateAsync(OperationType kind, IReadOnlyList<string> nodeIds,
        RollingOperationPolicy policy, Guid? initiatedBy, string actor, CancellationToken ct = default)
    {
        if (kind is not (OperationType.RollingMaintenance or OperationType.RollingUpgrade))
            throw new OperationStateException($"Unsupported rolling operation type {kind}");
        if (nodeIds.Count == 0)
            throw new OperationStateException("Node list is empty");
        if (kind == OperationType.RollingUpgrade && string.IsNullOrWhiteSpace(policy.TargetVersion))
            throw new OperationStateException("TargetVersion is required for rolling upgrades");

        var nodes = await db.Nodes.AsNoTracking()
            .Where(n => nodeIds.Contains(n.NodeId)).ToListAsync(ct);
        var missing = nodeIds.Except(nodes.Select(n => n.NodeId)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException($"Nodes not found: {string.Join(", ", missing)}", "NODE_NOT_FOUND");
        var notActive = nodes.Where(n => n.LifecycleState != NodeLifecycleState.Active).ToList();
        if (notActive.Count > 0)
            throw new OperationStateException(
                $"Nodes not Active: {string.Join(", ", notActive.Select(n => n.NodeId))}");

        var busy = await db.OperationSteps.AsNoTracking()
            .Where(s => nodeIds.Contains(s.NodeId) && NonTerminalStepStatuses.Contains(s.Status))
            .Select(s => s.NodeId).Distinct().ToListAsync(ct);
        if (busy.Count > 0)
            throw new OperationStateException(
                $"Nodes already in a rolling operation: {string.Join(", ", busy)}");

        var waveSize = policy.WaveSize
            ?? Math.Max(1, (int)Math.Ceiling(nodeIds.Count * (policy.WavePercent ?? 100) / 100.0));

        var operationId = await operations.CreateAsync(
            kind, referenceId: null, initiatedBy, OperationSource.User,
            correlationId: Guid.NewGuid().ToString(),
            canCancel: true, canRetry: false,
            summary: $"{kind} across {nodeIds.Count} node(s), wave size {waveSize}, by {actor}",
            metadataJson: RollingOperationPolicy.ToJson(policy), ct);

        var wave = 0;
        foreach (var chunk in nodeIds.Chunk(waveSize))
        {
            wave++;
            foreach (var nodeId in chunk)
                db.OperationSteps.Add(new SyncOperationStep
                {
                    StepId = Guid.NewGuid(), OperationId = operationId, NodeId = nodeId,
                    WaveNumber = wave, Status = nameof(RollingStepStatus.Pending),
                });
        }
        await db.SaveChangesAsync(ct);
        return operationId;
    }

    public async Task PauseAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await RequireOpAsync(operationId, ct);
        if (op.Status != nameof(OperationStatus.Running))
            throw new OperationStateException($"Cannot pause operation in status {op.Status}");
        op.Status = "Paused";
        await db.SaveChangesAsync(ct);
    }

    public async Task ResumeAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await RequireOpAsync(operationId, ct);
        if (op.Status != "Paused")
            throw new OperationStateException($"Cannot resume operation in status {op.Status}");
        op.Status = nameof(OperationStatus.Running);
        await db.SaveChangesAsync(ct);
    }

    public async Task AbortAsync(Guid operationId, string actor, CancellationToken ct = default)
    {
        var op = await RequireOpAsync(operationId, ct);
        if (op.Status is not (nameof(OperationStatus.Pending) or nameof(OperationStatus.Running) or "Paused"))
            throw new OperationStateException($"Cannot abort operation in status {op.Status}");

        var steps = await db.OperationSteps
            .Where(s => s.OperationId == operationId).ToListAsync(ct);
        foreach (var step in steps)
        {
            switch (Enum.Parse<RollingStepStatus>(step.Status))
            {
                case RollingStepStatus.Pending:
                    step.Status = nameof(RollingStepStatus.Skipped);
                    step.CompletedAt = DateTime.UtcNow;
                    break;
                case RollingStepStatus.Draining:
                case RollingStepStatus.InMaintenance:
                case RollingStepStatus.AwaitingVerification:
                    await RestoreNodeAsync(step.NodeId, actor, ct);
                    step.Status = nameof(RollingStepStatus.Skipped);
                    step.CompletedAt = DateTime.UtcNow;
                    break;
            }
        }
        op.Status = nameof(OperationStatus.Cancelled);
        op.Result = "Cancelled";
        op.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmStepAsync(Guid stepId, CancellationToken ct = default)
    {
        var step = await db.OperationSteps.FirstOrDefaultAsync(s => s.StepId == stepId, ct)
            ?? throw new NotFoundException($"Step {stepId} not found", "STEP_NOT_FOUND");
        if (step.Status != nameof(RollingStepStatus.InMaintenance))
            throw new OperationStateException($"Cannot confirm step in status {step.Status}");
        step.Status = nameof(RollingStepStatus.AwaitingVerification);
        await db.SaveChangesAsync(ct);
    }

    private async Task<SyncOperation> RequireOpAsync(Guid id, CancellationToken ct)
        => await db.Operations.FirstOrDefaultAsync(o => o.OperationId == id, ct)
           ?? throw new NotFoundException($"Operation {id} not found", "OPERATION_NOT_FOUND");

    private async Task RestoreNodeAsync(string nodeId, string actor, CancellationToken ct)
    {
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node is null) return;
        if (node.MaintenanceMode)
            await lifecycle.EndMaintenanceAsync(nodeId, "Rolling operation aborted", actor, ct);
        if (node.LifecycleState == NodeLifecycleState.Draining)
            await lifecycle.ResumeFromDrainAsync(nodeId, "Rolling operation aborted", actor, ct);
    }
}
```

Notes for implementer:
- Verify `OperationStatus` enum members (`Pending|Running|Completed|Failed|Cancelled`) — "Paused" is NOT in the enum; it is stored as a plain string status for rolling ops only (12C grid shows raw status strings). Confirm `OperationsController`/frontend tolerate unknown status strings; if the frontend switch is exhaustive, Task 8 adds the `Paused` badge.
- Verify `EndMaintenanceAsync` exact signature in `INodeLifecycleService` and match.
- Verify `OperationSource.User` member name.
- For maintenance-confirm flow: `ConfirmStepAsync` moves to `AwaitingVerification`; the worker (Task 6) treats `AwaitingVerification` in a maintenance op as "clear flag + resume + Completed" (no version check).

- [ ] **Step 5: Run tests**

```powershell
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~RollingOperationServiceTests" --nologo
```

Expected: PASS. Then full suite: `dotnet test tests/MSOSync.MetadataTests --nologo` — green.

- [ ] **Step 6: DI + commit**

Register in Metadata DI extension: `services.AddScoped<IRollingOperationService, RollingOperationService>();`

```powershell
git add src/MSOSync.Common/Exceptions/OperationStateException.cs src/MSOSync.Metadata/Operations/Rolling/ src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs src/MSOSync.Metadata/ tests/MSOSync.MetadataTests/Operations/RollingOperationServiceTests.cs
git commit -m "feat(2B.1-T4): RollingOperationService with wave assignment, abort restore, step confirm"
```
