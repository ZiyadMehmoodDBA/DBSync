using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/operations")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class OperationsController(
    IOperationQueryService queryService,
    IOperationService      operationService,
    IPermissionService     permissions) : ControllerBase
{
    private string Actor => User.Identity?.Name
        ?? throw new UnauthorizedException("No identity", "UNAUTHORIZED");

    private Guid ActorId
    {
        get
        {
            var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var g) ? g : Guid.Empty;
        }
    }

    // ── GET /api/v1/operations ─────────────────────────────────────────────────

    /// <summary>
    /// Returns a cursor-paginated list of operations, newest first.
    /// </summary>
    /// <param name="types">Comma-separated operation types to include (Export, Rollout, Decommission, Recovery).</param>
    /// <param name="statuses">Comma-separated statuses to include (Pending, Running, Completed, Failed, Cancelled).</param>
    /// <param name="sources">Comma-separated sources to include (User, System, Scheduler, Worker, Api).</param>
    /// <param name="from">Inclusive lower bound on started_at (UTC).</param>
    /// <param name="to">Inclusive upper bound on started_at (UTC).</param>
    /// <param name="initiatedBy">Filter by the user GUID who initiated the operation.</param>
    /// <param name="cursor">Opaque cursor returned by the previous page response.</param>
    /// <param name="pageSize">Number of items per page (1–100, default 25).</param>
    [HttpGet]
    [ProducesResponseType(typeof(OperationPageDto), 200)]
    public async Task<IActionResult> GetOperations(
        [FromQuery] string?   types       = null,
        [FromQuery] string?   statuses    = null,
        [FromQuery] string?   sources     = null,
        [FromQuery] DateTime? from        = null,
        [FromQuery] DateTime? to          = null,
        [FromQuery] string?   initiatedBy = null,
        [FromQuery] string?   cursor      = null,
        [FromQuery] int       pageSize    = 25,
        CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 100)
            return BadRequest(new { error = "pageSize must be between 1 and 100." });

        var filter = new OperationFilter(
            Types:       SplitCsv(types),
            Statuses:    SplitCsv(statuses),
            Sources:     SplitCsv(sources),
            From:        from,
            To:          to,
            InitiatedBy: initiatedBy,
            Cursor:      cursor,
            PageSize:    pageSize);

        var result = await queryService.GetPageAsync(filter, ct);
        return Ok(result);
    }

    // ── GET /api/v1/operations/{id} ────────────────────────────────────────────

    /// <summary>Returns the detail view of a single operation.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OperationDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetOperation(Guid id, CancellationToken ct)
    {
        var detail = await queryService.GetDetailAsync(id, ct);
        if (detail is null) return NotFound(new { error = $"Operation {id} not found." });
        return Ok(detail);
    }

    // ── POST /api/v1/operations/{id}/cancel ───────────────────────────────────

    /// <summary>
    /// Cancels a Pending or Running operation.
    /// Returns 409 if the operation is already in a terminal state or does not support cancellation.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(OperationDetailDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CancelOperation(Guid id, CancellationToken ct)
    {
        if (!await HasManagePermissionAsync(ct))
            return Forbid();

        try
        {
            await operationService.CancelAsync(id, ActorId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Operation {id} not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        var updated = await queryService.GetDetailAsync(id, ct);
        return Ok(updated);
    }

    // ── POST /api/v1/operations/{id}/retry ────────────────────────────────────

    /// <summary>
    /// Retries a Failed or Cancelled operation.
    /// Returns 409 if the operation is not retryable or does not support retry.
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(typeof(OperationDetailDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> RetryOperation(Guid id, CancellationToken ct)
    {
        if (!await HasManagePermissionAsync(ct))
            return Forbid();

        try
        {
            await operationService.RetryAsync(id, ActorId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Operation {id} not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        var updated = await queryService.GetDetailAsync(id, ct);
        return Ok(updated);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the caller holds either ManageConfigurations or ManageNodeLifecycle.
    /// Matching the pattern used by NodeLifecycleController for multi-permission checks.
    /// </summary>
    private async Task<bool> HasManagePermissionAsync(CancellationToken ct)
    {
        var effective = await permissions.GetEffectivePermissionsAsync(Actor, ct);
        return effective.Permissions.Contains(SystemPermissions.ManageConfigurations)
            || effective.Permissions.Contains(SystemPermissions.ManageNodeLifecycle);
    }

    private static string[]? SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
