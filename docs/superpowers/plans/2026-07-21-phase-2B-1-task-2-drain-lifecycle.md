# 2B.1 Task 2 — Drain Lifecycle (state machine, commands, endpoints)

**Files:**
- Modify: `src/MSOSync.Metadata/Lifecycle/NodeLifecycleStateMachine.cs`
- Modify: `src/MSOSync.Metadata/NodeManagement/NodeLifecycleService.cs` (+ its interface `INodeLifecycleService`)
- Modify: `src/MSOSync.Metadata/NodeManagement/NodeManagementAuditActions.cs` (locate: `grep -rn "NodeDisabled" src/MSOSync.Metadata`)
- Modify: transition metadata provider (locate: `grep -rln "ITransitionMetadataProvider" src/`)
- Create: `src/MSOSync.Metadata/NodeManagement/INodeReadQueryService.cs` + `NodeReadQueryService.cs`
- Modify: `src/MSOSync.Api/Controllers/NodeLifecycleController.cs`
- Modify: Metadata DI wiring (locate: `grep -rn "INodeLifecycleService" src/MSOSync.Metadata/*Extensions*`)
- Test: `tests/MSOSync.MetadataTests/Lifecycle/NodeLifecycleStateMachineTests.cs` (existing file — add cases)
- Test: `tests/MSOSync.MetadataTests/NodeManagement/NodeLifecycleServiceTests.cs` (existing — add cases; follow existing fixture pattern in that file)

**Interfaces:**
- Consumes: Task 1 (`NodeLifecycleState.Draining`, `SyncNode.DrainCompletedAt`).
- Produces:
  - Transitions: `Active → Draining`, `Draining → Active`, `Draining → Decommissioning`
  - `INodeLifecycleService.StartDrainAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default)`
  - `INodeLifecycleService.ResumeFromDrainAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default)`
  - Audit actions: `NodeManagementAuditActions.NodeDrainStarted = "NODE_DRAIN_STARTED"`, `NodeDrainResumed = "NODE_DRAIN_RESUMED"`, `NodeDrainCompleted = "NODE_DRAIN_COMPLETED"` (constant used by Task 6)
  - Endpoints: `POST api/v1/node-lifecycle/nodes/{id}/drain`, `POST .../nodes/{id}/resume-drain`
  - `INodeReadQueryService.GetNodeAsync(string nodeId, CancellationToken ct)` returning `SyncNode?` (AsNoTracking)

- [ ] **Step 1: Failing state-machine tests**

Append to `NodeLifecycleStateMachineTests.cs` (match existing test style in file):

```csharp
[Theory]
[InlineData(NodeLifecycleState.Active,   NodeLifecycleState.Draining,        true)]
[InlineData(NodeLifecycleState.Draining, NodeLifecycleState.Active,          true)]
[InlineData(NodeLifecycleState.Draining, NodeLifecycleState.Decommissioning, true)]
[InlineData(NodeLifecycleState.Draining, NodeLifecycleState.Disabled,        false)]
[InlineData(NodeLifecycleState.Disabled, NodeLifecycleState.Draining,        false)]
public void CanTransition_draining_matrix(NodeLifecycleState from, NodeLifecycleState to, bool expected)
    => new NodeLifecycleStateMachine().CanTransition(from, to).Should().Be(expected);
```

Run: `dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~NodeLifecycleStateMachineTests" --nologo`
Expected: FAIL (KeyNotFoundException for `Draining` / false where true expected).

- [ ] **Step 2: Extend transition table**

In `NodeLifecycleStateMachine.cs`:

```csharp
[NodeLifecycleState.Active] =
    [NodeLifecycleState.Disabled, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning, NodeLifecycleState.Draining],
[NodeLifecycleState.Draining] =
    [NodeLifecycleState.Active, NodeLifecycleState.Decommissioning],
```

Run same filter. Expected: PASS.

- [ ] **Step 3: Audit action constants**

In `NodeManagementAuditActions` add:

```csharp
public const string NodeDrainStarted   = "NODE_DRAIN_STARTED";
public const string NodeDrainResumed   = "NODE_DRAIN_RESUMED";
public const string NodeDrainCompleted = "NODE_DRAIN_COMPLETED";
```

- [ ] **Step 4: Service commands (mirror DisableAsync pattern)**

`INodeLifecycleService` + `NodeLifecycleService`:

```csharp
public Task StartDrainAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default)
    => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Draining, LifecycleTrigger.Manual,
        actorUsername, reason, NodeManagementAuditActions.NodeDrainStarted,
        mutate: (node, _) => { node.DrainCompletedAt = null; return Task.CompletedTask; }, ct: ct);

public Task ResumeFromDrainAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default)
    => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Active, LifecycleTrigger.Manual,
        actorUsername, reason, NodeManagementAuditActions.NodeDrainResumed,
        mutate: (node, _) => { node.DrainCompletedAt = null; return Task.CompletedTask; }, ct: ct);
```

Check `ExecuteTransitionAsync`'s `mutate` delegate signature (`Func<SyncNode, NodeLifecycleState, Task>?`) and adjust lambda shape if needed. Add service-level tests to `NodeLifecycleServiceTests.cs` following that file's existing fixture: assert `StartDrainAsync` moves Active→Draining, clears `DrainCompletedAt`, writes history; `ResumeFromDrainAsync` Draining→Active; drain from `Disabled` throws `InvalidLifecycleTransitionException`.

- [ ] **Step 5: Transition metadata**

In `TransitionMetadataProvider.GetTransitions` (file: `src/MSOSync.Metadata/Lifecycle/TransitionMetadataProvider.cs`), the switch iterates `stateMachine.AllowedTargets(node.LifecycleState)`. Convention: PascalCase action names, `DangerLevel` is `"Normal"` or `"Critical"` (positional record `TransitionActionDto(Action, RequiresReason, RequiresConfirmation, DangerLevel)`). Add two cases:

```csharp
case NodeLifecycleState.Draining when node.LifecycleState == NodeLifecycleState.Active:
    actions.Add(new("StartDrain", false, true, "Normal"));
    break;
case NodeLifecycleState.Active when node.LifecycleState == NodeLifecycleState.Draining:
    actions.Add(new("ResumeDrain", false, false, "Normal"));
    break;
```

The existing `case NodeLifecycleState.Decommissioning:` already emits `"Decommission"` — Draining→Decommissioning gets it for free, no change. Note the existing `case NodeLifecycleState.Active when ... Disabled` ("Enable") stays; C# allows multiple guarded cases on the same target.

Frontend contract (Task 8 consumes): actions `"StartDrain"` / `"ResumeDrain"` extend the `LifecycleAction` union in `src/MSOSync.Frontend/src/shared/types/lifecycle.ts`.

- [ ] **Step 6: Node-read query service (2A-029 part 1)**

`src/MSOSync.Metadata/NodeManagement/INodeReadQueryService.cs`:

```csharp
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public interface INodeReadQueryService
{
    Task<SyncNode?> GetNodeAsync(string nodeId, CancellationToken ct = default);
}
```

`NodeReadQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed class NodeReadQueryService(AppDbContext db) : INodeReadQueryService
{
    public Task<SyncNode?> GetNodeAsync(string nodeId, CancellationToken ct = default)
        => db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}
```

Register scoped in the Metadata DI extension next to `INodeLifecycleService`.

- [ ] **Step 7: Controller — endpoints + drop AppDbContext**

`NodeLifecycleController`: replace `AppDbContext db` ctor param with `INodeReadQueryService nodeRead`; replace the `db.Nodes.AsNoTracking()...` read in `GetTransitions` (and any other direct read in this controller) with:

```csharp
var node = await nodeRead.GetNodeAsync(id, ct)
    ?? throw new NotFoundException($"Node {id} not found", "NODE_NOT_FOUND");
```

Add endpoints (mirror `Disable`; `DrainRequest`/`ResumeDrainRequest` records go in the same Dtos location as `DisableRequest` — find it, don't inline in controller, RULE-DTO-1):

```csharp
[HttpPost("nodes/{id}/drain")]
[ProducesResponseType(204)]
[ProducesResponseType(404)]
[ProducesResponseType(409)]
public async Task<IActionResult> Drain(string id, [FromBody] DrainRequest req, CancellationToken ct)
{
    await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
    await lifecycle.StartDrainAsync(id, req.Reason, Actor, ct);
    return NoContent();
}

[HttpPost("nodes/{id}/resume-drain")]
[ProducesResponseType(204)]
[ProducesResponseType(404)]
[ProducesResponseType(409)]
public async Task<IActionResult> ResumeDrain(string id, [FromBody] ResumeDrainRequest req, CancellationToken ct)
{
    await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
    await lifecycle.ResumeFromDrainAsync(id, req.Reason, Actor, ct);
    return NoContent();
}
```

```csharp
public sealed record DrainRequest(string? Reason);
public sealed record ResumeDrainRequest(string? Reason);
```

Validators (in `src/MSOSync.Api/Validators/`, mirror `ApproveRegistrationRequestValidator`):

```csharp
public sealed class DrainRequestValidator : AbstractValidator<DrainRequest>
{
    public DrainRequestValidator()
        => RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
}

public sealed class ResumeDrainRequestValidator : AbstractValidator<ResumeDrainRequest>
{
    public ResumeDrainRequestValidator()
        => RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
}
```

- [ ] **Step 8: Run tests + commit**

```powershell
dotnet test tests/MSOSync.MetadataTests --nologo
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: green, 0 warnings.

```powershell
git add src/MSOSync.Metadata/ src/MSOSync.Api/ tests/MSOSync.MetadataTests/
git commit -m "feat(2B.1-T2): Draining transitions, drain/resume commands + endpoints, node-read query service"
```
