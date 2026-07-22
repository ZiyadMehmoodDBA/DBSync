using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Authorization;
using MSOSync.Api.Dtos.Requests;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Operations.Replay;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/operations/replay")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ReplayController(
    IReplayOperationService      replay,
    IReplayOperationQueryService query,
    INodeAuthorizationService    authz) : ControllerBase
{
    private Guid? ActorId => User.Claims
        .Where(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)
        .Select(c => Guid.TryParse(c.Value, out var id) ? (Guid?)id : null)
        .FirstOrDefault();

    [HttpPost]
    [ProducesResponseType(typeof(ReplayOperationCreatedDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create(
        [FromBody] CreateReplayOperationRequest req, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);

        var svcReq = new CreateReplayRequest(
            req.NodeId, req.ReplayMode,
            req.FromTime, req.ToTime,
            req.ChannelIds, req.BatchIds,
            InitiatedBy: ActorId);

        var result = await replay.CreateAsync(svcReq, ct);
        return CreatedAtAction(nameof(GetDetail), new { id = result.OperationId }, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ReplayOperationDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var detail = await query.GetDetailAsync(id, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("{id:guid}/items")]
    [ProducesResponseType(typeof(CursorPageResult<ReplayItemDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetItems(
        Guid id, [FromQuery] string? status,
        [FromQuery] string? cursor, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        var filter = new ReplayItemFilter(status, cursor, pageSize);
        var result = await query.GetItemsAsync(id, filter, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageNodeLifecycle, ct);
        await replay.CancelAsync(id, ct);
        return NoContent();
    }
}
