using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/node-management")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class NodeManagementController(
    INodeManagementService             nodeManagement,
    INodeLifecycleService              lifecycle,
    IPermissionService                 permissionService,
    ICurrentUserService                currentUser,
    IValidator<RegistrationFilter>     listValidator)
    : ControllerBase
{
    // ── Registration read ──────────────────────────────────────────────────────

    [HttpGet("registrations")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> GetRegistrations(
        [FromQuery] RegistrationFilter filter, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ViewTopology))
            return Forbid();

        await listValidator.ValidateAndThrowAsync(filter, ct);
        return Ok(await nodeManagement.GetRegistrationsAsync(filter, ct));
    }

    [HttpGet("registrations/{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetRegistrationDetail(long id, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ViewTopology))
            return Forbid();

        var dto = await nodeManagement.GetRegistrationByIdAsync(id, ct);
        if (dto is null) throw new NotFoundException($"Registration {id} not found.");
        return Ok(dto);
    }

    // ── Inbound registration (agent-facing, no UI auth) ───────────────────────

    [HttpPost("registrations")]
    [AllowAnonymous]
    [ProducesResponseType(202)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> InboundRegistration(
        [FromBody] InboundRegistrationDto dto, CancellationToken ct)
    {
        var id = await lifecycle.RegisterAsync(dto, ct);
        return StatusCode(202, new { registrationId = id });
    }

    // ── Approve / Reject ───────────────────────────────────────────────────────

    [HttpPost("registrations/{id:long}/approve")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> ApproveRegistration(
        long id, [FromBody] ApproveRegistrationRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        await lifecycle.ApproveAsync(id, request.Notes, currentUser.GetCurrentUsername(), ct);
        return NoContent();
    }

    [HttpPost("registrations/{id:long}/reject")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> RejectRegistration(
        long id, [FromBody] RejectRegistrationRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        await lifecycle.RejectAsync(id, request.Reason, currentUser.GetCurrentUsername(), ct);
        return NoContent();
    }

    // ── Bulk ───────────────────────────────────────────────────────────────────

    [HttpPost("registrations/bulk-approve")]
    [ProducesResponseType(207)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> BulkApprove(
        [FromBody] BulkApproveRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        var results = await lifecycle.BulkApproveAsync(
            request.Ids, currentUser.GetCurrentUsername(), ct);
        return StatusCode(207, results);
    }

    [HttpPost("registrations/bulk-reject")]
    [ProducesResponseType(207)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> BulkReject(
        [FromBody] BulkRejectRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        var results = await lifecycle.BulkRejectAsync(
            request.Ids, request.Reason, currentUser.GetCurrentUsername(), ct);
        return StatusCode(207, results);
    }
}
