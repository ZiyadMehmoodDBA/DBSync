using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Authorization;
using MSOSync.Api.Dtos.Operations;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Rolling;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/operations/rolling")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class RollingOperationsController(
    IRollingOperationService      rolling,
    IRollingOperationQueryService query,
    INodeAuthorizationService     authz) : ControllerBase
{
    private string Actor => User.Identity?.Name
        ?? throw new UnauthorizedException("No identity", "UNAUTHORIZED");

    [HttpPost]
    [ProducesResponseType(typeof(CreateRollingOperationResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create([FromBody] CreateRollingOperationRequest req, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var policy = new RollingOperationPolicy(req.WaveSize, req.WavePercent, req.GateSoakSeconds,
            req.WaveAction, req.WindowSeconds, req.TargetVersion, req.VerificationTimeoutSeconds);
        var kind = Enum.Parse<OperationType>(req.Kind);
        var id = await rolling.CreateAsync(kind, req.NodeIds, policy, initiatedBy: null, Actor, ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreateRollingOperationResponse(id));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RollingOperationDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RollingOperationDetailDto>> Get(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        return Ok(await query.GetDetailAsync(id, ct));
    }

    [HttpPost("{id:guid}/pause")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.PauseAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/resume")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.ResumeAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/abort")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Abort(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.AbortAsync(id, Actor, ct);
        return NoContent();
    }

    [HttpPost("steps/{stepId:guid}/confirm")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> ConfirmStep(Guid stepId, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await rolling.ConfirmStepAsync(stepId, ct);
        return NoContent();
    }
}
