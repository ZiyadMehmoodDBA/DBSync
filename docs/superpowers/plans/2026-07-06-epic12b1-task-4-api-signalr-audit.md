# Epic 12B-1 Task 4: API Controllers + SignalR + Authorization

> Task 4 of 7. Master plan: `2026-07-06-epic12b1-node-lifecycle-engine.md`. Spec §7, §8, §10. Global Constraints apply. Requires Tasks 1–3.

**Goal:** Expose the lifecycle engine over HTTP (`NodeLifecycleController`, `POST /nodes/activate`), publish lifecycle/maintenance events to the SignalR "operators" group, centralize authorization in `NodeAuthorizationService`, and ship the transitions-metadata contract the frontend consumes.

**Files:**
- Create: `src/MSOSync.Api/Controllers/NodeLifecycleController.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/LifecycleRequestDtos.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/LifecycleRequestValidators.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/ITransitionMetadataProvider.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/TransitionMetadataProvider.cs`
- Create: `src/MSOSync.Api/Authorization/NodeAuthorizationService.cs` (namespace/folder: match where existing auth helpers live — if `MSOSync.Api` has no Authorization folder, create it)
- Modify: `src/MSOSync.Api/Controllers/NodesController.cs` (add `POST activate`)
- Modify: `src/MSOSync.App/SignalR/NodeOperationsPublisher.cs` (+2 handlers, +event types)
- Modify: `src/MSOSync.App/SignalR/OperationsEventType` + `OperationsEvent` definitions (wherever they live — grep `OperationsEventType`)
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs` (DI: metadata provider, validators)
- Modify: `src/MSOSync.Metadata/NodeManagement/NodeManagementController` route gates (provision endpoints → `PROVISION_NODES`)
- Test: `tests/MSOSync.MetadataTests/Lifecycle/TransitionMetadataProviderTests.cs`
- Test: `tests/MSOSync.MetadataTests/Lifecycle/LifecycleRequestValidatorTests.cs`

**Interfaces:**
- Consumes: `INodeLifecycleService` commands (Task 2), `INodeLifecycleHistoryService`, `INodeLifecycleStateMachine.AllowedTargets`, `NodeStateDto`, events (Task 2), existing permission-check mechanism used by `NodeManagementController`, existing rate-limiting infrastructure (Epic 8).
- Produces (Tasks 5–6 rely on):
  - Routes + status codes exactly as the table in Step 3
  - `TransitionsDto { CurrentState, AllowedTransitions: TransitionActionDto[] }` with `TransitionActionDto(string Action, bool RequiresReason, bool RequiresConfirmation, string DangerLevel)`
  - SignalR event types: `NodeLifecycleChanged`, `NodeMaintenanceChanged` (category routing contract, Step 6)
  - `OperationsEvent` gains optional `correlationId` and `trigger` fields

---

## Steps

- [ ] **Step 1: Failing tests — transition metadata + validators**

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/TransitionMetadataProviderTests.cs
using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class TransitionMetadataProviderTests
{
    private readonly TransitionMetadataProvider _provider = new(new NodeLifecycleStateMachine());

    private static SyncNode Node(NodeLifecycleState s, bool maintenance = false) => new()
    {
        NodeId = "n1", GroupId = "g", SyncUrl = "http://x",
        LifecycleState = s, MaintenanceMode = maintenance,
    };

    [Fact]
    public void Active_NoMaintenance_OffersDisableStartMaintenanceDecommission()
        => _provider.GetTransitions(Node(NodeLifecycleState.Active)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["Disable", "StartMaintenance", "Decommission"]);

    [Fact]
    public void Active_InMaintenance_OffersEndMaintenance_NotStart()
    {
        var actions = _provider.GetTransitions(Node(NodeLifecycleState.Active, maintenance: true))
            .AllowedTransitions.Select(t => t.Action).ToList();
        actions.Should().Contain("EndMaintenance");
        actions.Should().NotContain("StartMaintenance");
    }

    [Fact]
    public void Disabled_OffersEnableAndDecommission_Only()
        => _provider.GetTransitions(Node(NodeLifecycleState.Disabled)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["Enable", "Decommission"]);

    [Fact]
    public void Decommissioning_OffersForceCompleteOnly()
        => _provider.GetTransitions(Node(NodeLifecycleState.Decommissioning)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["ForceCompleteDecommission"]);

    [Theory]
    [InlineData(NodeLifecycleState.Decommissioned)]
    [InlineData(NodeLifecycleState.Rejected)]
    public void Terminal_OffersNothing(NodeLifecycleState s)
        => _provider.GetTransitions(Node(s)).AllowedTransitions.Should().BeEmpty();

    [Fact]
    public void Decommission_IsCritical_RequiresReasonAndConfirmation()
    {
        var t = _provider.GetTransitions(Node(NodeLifecycleState.Active))
            .AllowedTransitions.Single(x => x.Action == "Decommission");
        t.Should().Be(new TransitionActionDto("Decommission", true, true, "Critical"));
    }

    [Fact]
    public void StartMaintenance_RequiresReason_NoConfirmation_Normal()
    {
        var t = _provider.GetTransitions(Node(NodeLifecycleState.Active))
            .AllowedTransitions.Single(x => x.Action == "StartMaintenance");
        t.Should().Be(new TransitionActionDto("StartMaintenance", true, false, "Normal"));
    }

    [Fact]
    public void PendingRegistration_OffersDecommissionOnly()   // Activate is node-driven, never an operator action
        => _provider.GetTransitions(Node(NodeLifecycleState.PendingRegistration)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["Decommission"]);
}
```

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/LifecycleRequestValidatorTests.cs
using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class LifecycleRequestValidatorTests
{
    [Fact]
    public void MaintenanceStart_EmptyReason_Fails()
        => new MaintenanceStartRequestValidator()
            .Validate(new MaintenanceStartRequest("", null, false)).IsValid.Should().BeFalse();

    [Fact]
    public void MaintenanceStart_WithReason_Passes()
        => new MaintenanceStartRequestValidator()
            .Validate(new MaintenanceStartRequest("patching", null, true)).IsValid.Should().BeTrue();

    [Fact]
    public void Decommission_EmptyReason_Fails()
        => new DecommissionRequestValidator()
            .Validate(new DecommissionRequest("", null)).IsValid.Should().BeFalse();

    [Fact]
    public void Decommission_NegativeGrace_Fails()
        => new DecommissionRequestValidator()
            .Validate(new DecommissionRequest("site closure", -5)).IsValid.Should().BeFalse();

    [Fact]
    public void Activate_MissingFields_Fails()
        => new ActivateRequestValidator()
            .Validate(new ActivateRequest("", "", "")).IsValid.Should().BeFalse();
}
```

Run — expected FAIL (types missing):

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"; $env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~TransitionMetadataProviderTests|FullyQualifiedName~LifecycleRequestValidatorTests" -c Debug
```

- [ ] **Step 2: Request DTOs + validators + transition metadata provider**

```csharp
// src/MSOSync.Metadata/Lifecycle/LifecycleRequestDtos.cs
namespace MSOSync.Metadata.Lifecycle;

public sealed record DisableRequest(string? Reason);
public sealed record MaintenanceStartRequest(string Reason, DateTimeOffset? ExpectedEndAt, bool NotifyNode);
public sealed record DecommissionRequest(string Reason, int? GracePeriodMinutes);
public sealed record ActivateRequest(string ExternalId, string BootstrapToken, string AgentVersion);

public sealed record TransitionActionDto(
    string Action, bool RequiresReason, bool RequiresConfirmation, string DangerLevel);

public sealed record TransitionsDto(string CurrentState, IReadOnlyList<TransitionActionDto> AllowedTransitions);
```

```csharp
// src/MSOSync.Metadata/Lifecycle/LifecycleRequestValidators.cs
using FluentValidation;

namespace MSOSync.Metadata.Lifecycle;

public sealed class MaintenanceStartRequestValidator : AbstractValidator<MaintenanceStartRequest>
{
    public MaintenanceStartRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(512);
    }
}

public sealed class DecommissionRequestValidator : AbstractValidator<DecommissionRequest>
{
    public DecommissionRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(512);
        RuleFor(x => x.GracePeriodMinutes).GreaterThan(0).When(x => x.GracePeriodMinutes is not null);
    }
}

public sealed class DisableRequestValidator : AbstractValidator<DisableRequest>
{
    public DisableRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(512);
    }
}

public sealed class ActivateRequestValidator : AbstractValidator<ActivateRequest>
{
    public ActivateRequestValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BootstrapToken).NotEmpty();
        RuleFor(x => x.AgentVersion).NotEmpty().MaximumLength(50);
    }
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/ITransitionMetadataProvider.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public interface ITransitionMetadataProvider
{
    TransitionsDto GetTransitions(SyncNode node);
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/TransitionMetadataProvider.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// Backend owns the workflow contract (spec §7.3): the frontend renders exactly what
/// this returns and encodes ZERO transition rules of its own.
public sealed class TransitionMetadataProvider(INodeLifecycleStateMachine stateMachine)
    : ITransitionMetadataProvider
{
    public TransitionsDto GetTransitions(SyncNode node)
    {
        var actions = new List<TransitionActionDto>();

        foreach (var target in stateMachine.AllowedTargets(node.LifecycleState))
        {
            switch (target)
            {
                case NodeLifecycleState.Active when node.LifecycleState == NodeLifecycleState.Disabled:
                    actions.Add(new("Enable", false, true, "Normal"));
                    break;
                case NodeLifecycleState.Disabled when node.LifecycleState == NodeLifecycleState.Active:
                    actions.Add(new("Disable", false, true, "Normal"));
                    break;
                case NodeLifecycleState.Decommissioning:
                    actions.Add(new("Decommission", true, true, "Critical"));
                    break;
                case NodeLifecycleState.Decommissioned:
                    actions.Add(new("ForceCompleteDecommission", false, true, "Critical"));
                    break;
                // Recovery entry is registration-driven; Active-via-activation is node-driven;
                // Rejected is registration-reject; none are operator grid actions.
            }
        }

        // Maintenance is not a transition (spec §4.3) but IS an allowed action (spec §7.3)
        if (node.LifecycleState == NodeLifecycleState.Active)
        {
            actions.Add(node.MaintenanceMode
                ? new("EndMaintenance", false, false, "Normal")
                : new("StartMaintenance", true, false, "Normal"));
        }

        return new TransitionsDto(node.LifecycleState.ToString(), actions);
    }
}
```

DI in `MetadataServiceExtensions.AddMetadata`:

```csharp
services.AddSingleton<ITransitionMetadataProvider, TransitionMetadataProvider>();
services.AddScoped<IValidator<Lifecycle.MaintenanceStartRequest>, Lifecycle.MaintenanceStartRequestValidator>();
services.AddScoped<IValidator<Lifecycle.DecommissionRequest>, Lifecycle.DecommissionRequestValidator>();
services.AddScoped<IValidator<Lifecycle.DisableRequest>, Lifecycle.DisableRequestValidator>();
services.AddScoped<IValidator<Lifecycle.ActivateRequest>, Lifecycle.ActivateRequestValidator>();
```

(Match the validator-registration style already used for `RegistrationListFilterValidator`.)

Run Step 1 tests — expected: PASS.

- [ ] **Step 3: NodeAuthorizationService + NodeLifecycleController**

The existing per-method check pattern (verified in `NodeManagementController.cs:32-34` and `ExportJobController.cs:28-30`) is:

```csharp
var perms = await permissionService.GetEffectivePermissionsAsync(currentUser.GetCurrentUsername(), ct);
if (!perms.Permissions.Contains(SystemPermissions.X)) return Forbid();
```

`NodeAuthorizationService` centralizes it (throwing instead of returning, so it composes in services too):

```csharp
// src/MSOSync.Api/Authorization/NodeAuthorizationService.cs
// Two explicit stages (spec §10):
//   1. Permission validation — this service (one implementation instead of nine copies).
//   2. Business rule validation — the state machine + NodeLifecycleService commands
//      (cannot enable a Rejected node, cannot decommission a terminal node, ...).
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Authorization;

public interface INodeAuthorizationService
{
    /// Throws ForbiddenOperationException (→ 403 via GlobalExceptionHandler) when the
    /// current user lacks the permission.
    Task EnsurePermissionAsync(string permissionKey, CancellationToken ct);
}

public sealed class NodeAuthorizationService(
    IPermissionService permissionService,
    ICurrentUserService currentUser) : INodeAuthorizationService
{
    public async Task EnsurePermissionAsync(string permissionKey, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(permissionKey))
            throw new ForbiddenOperationException(
                $"Missing permission {permissionKey}", "FORBIDDEN");
    }
}
```

(`ICurrentUserService` namespace: same one `ExportJobController` imports. `ForbiddenOperationException` constructor: match its actual (message, code) shape from `src/MSOSync.Common/Exceptions/`.) Since the user comes from `ICurrentUserService`, the controller calls below pass only the permission key — adjust the controller code accordingly: `await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);`

Register alongside other Api services (mirror wherever `NodeManagementController`'s dependencies are registered): `services.AddScoped<INodeAuthorizationService, NodeAuthorizationService>();`

```csharp
// src/MSOSync.Api/Controllers/NodeLifecycleController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Authorization;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/node-lifecycle")]
[Authorize(Policy = "ViewerOrAbove")]   // match NodeManagementController's controller-level policy exactly
public sealed class NodeLifecycleController(
    INodeLifecycleService lifecycle,
    INodeLifecycleHistoryService history,
    ITransitionMetadataProvider transitions,
    INodeAuthorizationService authz,
    AppDbContext db) : ControllerBase
{
    private string Actor => User.Identity?.Name
        ?? throw new UnauthorizedException("No identity", "UNAUTHORIZED");

    [HttpPost("nodes/{id}/enable")]
    public async Task<IActionResult> Enable(string id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await lifecycle.EnableAsync(id, Actor, ct);
        return NoContent();
    }

    [HttpPost("nodes/{id}/disable")]
    public async Task<IActionResult> Disable(string id, [FromBody] DisableRequest req, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await lifecycle.DisableAsync(id, req.Reason, Actor, ct);
        return NoContent();
    }

    [HttpPost("nodes/{id}/maintenance/start")]
    public async Task<IActionResult> StartMaintenance(string id, [FromBody] MaintenanceStartRequest req, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await lifecycle.StartMaintenanceAsync(id, req.Reason, req.ExpectedEndAt, req.NotifyNode, Actor, ct);
        return NoContent();
    }

    [HttpPost("nodes/{id}/maintenance/end")]
    public async Task<IActionResult> EndMaintenance(string id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await lifecycle.EndMaintenanceAsync(id, Actor, ct);
        return NoContent();
    }

    [HttpPost("nodes/{id}/decommission")]
    public async Task<IActionResult> Decommission(string id, [FromBody] DecommissionRequest req, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await lifecycle.DecommissionAsync(id, req.Reason, req.GracePeriodMinutes, Actor, ct);
        return Accepted();   // 202 — drain continues asynchronously (spec §7.2)
    }

    [HttpPost("nodes/{id}/decommission/force")]
    public async Task<IActionResult> ForceCompleteDecommission(string id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await lifecycle.ForceCompleteDecommissionAsync(id, Actor, ct);
        return NoContent();
    }

    [HttpGet("nodes/{id}/state")]
    public async Task<ActionResult<NodeStateDto>> GetState(string id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ViewTopology, ct);
        return Ok(await history.GetCurrentStateAsync(id, ct));
    }

    [HttpGet("nodes/{id}/transitions")]
    public async Task<ActionResult<TransitionsDto>> GetTransitions(string id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ViewTopology, ct);
        var node = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.NodeId == id, ct)
            ?? throw new NotFoundException($"Node {id} not found", "NODE_NOT_FOUND");
        return Ok(transitions.GetTransitions(node));
    }

    [HttpGet("nodes/{id}/history")]
    public async Task<IActionResult> GetHistory(
        string id,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] LifecycleTrigger? trigger = null,   // using MSOSync.Persistence.Entities;
        CancellationToken ct = default)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ViewTopology, ct);
        var result = await history.GetTimelineAsync(
            id, new LifecycleHistoryFilter(from, to, trigger, page, Math.Clamp(pageSize, 1, 200)), ct);
        return Ok(result);
    }
}
```

(Model-validation: wire the FluentValidation validators the same way `NodeManagementController` validates request bodies — auto-validation middleware or explicit `validator.ValidateAndThrow`; copy that mechanism.)

- [ ] **Step 4: Activation endpoint (node-facing)**

In `src/MSOSync.Api/Controllers/NodesController.cs`, add (anonymous route — the bootstrap token IS the credential; apply the same rate-limiting attribute/policy used by the inbound registration endpoint — check `NodeManagementController`'s `[AllowAnonymous]` registration action for the exact attribute):

```csharp
[HttpPost("activate")]
[AllowAnonymous]
public async Task<ActionResult<ActivateResultDto>> Activate(
    [FromBody] ActivateRequest request, CancellationToken ct)
{
    var result = await lifecycleService.ActivateAsync(
        request.ExternalId, request.BootstrapToken, request.AgentVersion, ct);
    return Ok(result);
}
```

Inject `INodeLifecycleService lifecycleService` into `NodesController`'s constructor. Failure mapping is automatic via GlobalExceptionHandler: `UnauthorizedException` → 401 (invalid/consumed/revoked token, unknown ExternalId), `InvalidLifecycleTransitionException` → 409 (wrong state). Never log the token (the request DTO must not be logged wholesale).

Route check: `NodesController`'s route is `api/v1/nodes` → full path `POST /api/v1/nodes/activate` ✓ (spec §4.5). Auth boundary preserved: operator JWT endpoints deleted from this controller in Task 2; remaining actions are node-credential or anonymous-token routes plus reads — **verify**: any remaining operator-JWT action in `NodesController` (e.g. `GET` list used by the legacy nodes page, `POST` create, `PUT` update) must stay JWT-gated as-is; spec §7.1 separation applies to the lifecycle/activation routes, and `CreateNode`/`UpdateNode`/`GetNodes` remain the legacy admin CRUD surface until a future epic. Do not mix auth models on any single action.

- [ ] **Step 5: Provision permission re-gate**

In the controller hosting `POST /api/v1/node-management/provision` and `provision-package`: replace the `MANAGE_USERS` permission check with `SystemPermissions.ProvisionNodes`. (Deviation 2 in the master plan: `PROVISION_NODES` is new, seeded in M022, ADMIN-only by default.)

- [ ] **Step 6: SignalR — event types + publisher handlers**

Locate `OperationsEventType` and `OperationsEvent` (grep `OperationsEventType` — defined near `src/MSOSync.App/SignalR/`). Add two members:

```csharp
NodeLifecycleChanged,
NodeMaintenanceChanged,
```

Extend `OperationsEvent` with two optional fields (additive — existing consumers unaffected):

```csharp
public Guid? CorrelationId { get; init; }
public string? Trigger { get; init; }
```

In `src/MSOSync.App/SignalR/NodeOperationsPublisher.cs`, add handlers (mirror the existing `INotificationHandler<NodeConnectivityChangedEvent>` implementation style — same "operators" group, same `SendAsync("OperationsEvent", …)`):

```csharp
public async Task Handle(NodeLifecycleChangedEvent evt, CancellationToken ct)
{
    await hub.Clients.Group("operators").SendAsync("OperationsEvent", new OperationsEvent
    {
        Type = OperationsEventType.NodeLifecycleChanged,
        NodeId = evt.NodeId,
        PreviousStatus = evt.PreviousState.ToString(),
        CurrentStatus = evt.NewState.ToString(),
        OccurredAt = DateTimeOffset.UtcNow,
        CorrelationId = evt.CorrelationId,
        Trigger = evt.Trigger.ToString(),
    }, ct);
}

public async Task Handle(NodeMaintenanceChangedEvent evt, CancellationToken ct)
{
    await hub.Clients.Group("operators").SendAsync("OperationsEvent", new OperationsEvent
    {
        Type = OperationsEventType.NodeMaintenanceChanged,
        NodeId = evt.NodeId,
        CurrentStatus = evt.Enabled ? "MaintenanceOn" : "MaintenanceOff",
        OccurredAt = DateTimeOffset.UtcNow,
    }, ct);
}
```

Declare the interfaces on the class: `INotificationHandler<NodeLifecycleChangedEvent>, INotificationHandler<NodeMaintenanceChangedEvent>` and add `using MSOSync.Metadata.Events;`. Adapt property names/constructor shape to the actual `OperationsEvent` definition (it may be a positional record — extend accordingly; `NodeLabel`/`GroupId` may be looked up like existing handlers do — mirror them).

Note: the legacy `NodeMetadataChangedEvent` mappings ("APPROVED"→NodeApproved etc.) still fire from `NodeMetadataService` update paths — leave them; the lifecycle events are the new authoritative stream. If Task 2 deleted the only publishers of `"ENABLED"`/`"DISABLED"` actions, the corresponding mapping arms become dead but harmless — remove the dead arms only if the compiler/analyzer flags them.

**Best-effort node notification (spec §4.7 step 4, §4.3):** add a second MediatR handler — a fire-and-forget courtesy ping to the node itself when it enters `Decommissioning`. Create `src/MSOSync.App/SignalR/../` sibling file `src/MSOSync.App/Notifications/NodeDecommissionNotifier.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.App.Notifications;

/// Best-effort NODE_DECOMMISSIONING notice to the node (spec §4.7 step 4).
/// Failures are logged and swallowed — the drain does not depend on the node hearing this.
public sealed class NodeDecommissionNotifier(
    IHttpClientFactory httpClientFactory,
    AppDbContext db,
    ILogger<NodeDecommissionNotifier> logger) : INotificationHandler<NodeLifecycleChangedEvent>
{
    public async Task Handle(NodeLifecycleChangedEvent evt, CancellationToken ct)
    {
        if (evt.NewState != NodeLifecycleState.Decommissioning) return;

        var syncUrl = await db.Nodes.AsNoTracking()
            .Where(n => n.NodeId == evt.NodeId)
            .Select(n => n.SyncUrl)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(syncUrl)) return;

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            await client.PostAsJsonAsync(
                $"{syncUrl.TrimEnd('/')}/api/v1/sync/lifecycle-notice",
                new { type = "NODE_DECOMMISSIONING", nodeId = evt.NodeId }, ct);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex,
                "Best-effort decommission notice to node {NodeId} failed (expected if unreachable)", evt.NodeId);
        }
    }
}
```

(The node agent has no `lifecycle-notice` endpoint yet — a 404 is an acceptable best-effort outcome; the agent-side handler is future work. `IHttpClientFactory` is already registered if any typed/named client exists — otherwise add `services.AddHttpClient();` next to the app's existing HTTP registrations. Maintenance `notifyNode` uses the same channel — acceptable to defer that call-site to when the agent endpoint exists; record it in the task report.)

- [ ] **Step 7: Build + tests**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.MetadataTests -c Debug --no-build
```

Expected: zero warnings, all green. (HTTP-level behavior is asserted in Task 7's integration suites.)

- [ ] **Step 8: Commit**

```pwsh
git add src/MSOSync.Api/Controllers/NodeLifecycleController.cs src/MSOSync.Api/Controllers/NodesController.cs src/MSOSync.Api/Authorization
git add src/MSOSync.Metadata/Lifecycle src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.App/SignalR
git add tests/MSOSync.MetadataTests/Lifecycle/TransitionMetadataProviderTests.cs tests/MSOSync.MetadataTests/Lifecycle/LifecycleRequestValidatorTests.cs
git commit -m "feat(12B-1): NodeLifecycleController + activation endpoint, transitions metadata contract, lifecycle SignalR events, PROVISION_NODES gate"
```

(Also stage the file containing the provision-endpoint permission change and the `OperationsEvent` definition file, by name.)
