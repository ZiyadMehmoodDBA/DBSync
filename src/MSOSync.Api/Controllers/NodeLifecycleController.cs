using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Authorization;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/node-lifecycle")]
[Authorize(Policy = "ViewerOrAbove")]
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
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        return Ok(await history.GetCurrentStateAsync(id, ct));
    }

    [HttpGet("nodes/{id}/transitions")]
    public async Task<ActionResult<TransitionsDto>> GetTransitions(string id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var node = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.NodeId == id, ct)
            ?? throw new NotFoundException($"Node {id} not found", "NODE_NOT_FOUND");
        return Ok(transitions.GetTransitions(node));
    }

    [HttpGet("nodes/{id}/history")]
    public async Task<IActionResult> GetHistory(
        string id,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] LifecycleTrigger? trigger = null,
        CancellationToken ct = default)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var result = await history.GetTimelineAsync(
            id, new LifecycleHistoryFilter(from, to, trigger, page, Math.Clamp(pageSize, 1, 200)), ct);
        return Ok(result);
    }
}
