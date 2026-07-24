using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Dtos.Nodes;
using MSOSync.Common;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Options;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/nodes")]
public sealed class NodesController(
    INodeMetadataService        nodeService,
    IClock                      clock,
    INodeLifecycleService       lifecycleService,
    HeartbeatProcessor          heartbeatProcessor,
    IOptions<PaginationOptions> paginationOptions) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<NodeDto>), 200)]
    [ProducesResponseType(typeof(NodeListGateResponse), 200)]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
    {
        var threshold = paginationOptions.Value.NodeListCursorThreshold;
        var gate = await nodeService.GetNodesWithGateAsync(threshold, ct);

        if (!gate.PaginationRequired)
            return Ok(gate.Items);

        return Ok(new NodeListGateResponse(
            PaginationRequired: true,
            Items: Array.Empty<NodeDto>(),
            NextCursor: gate.NextCursor,
            CursorEndpoint: "/api/v1/nodes/cursor"));
    }

    [HttpGet("paged")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<NodeDto>), 200)]
    public async Task<IActionResult> GetNodesPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 50,
        CancellationToken ct = default)
    {
        Response.Headers.Append("Deprecation", "true");
        Response.Headers.Append("Link", "</api/v1/nodes/cursor>; rel=\"successor-version\"");

        pageSize = Math.Min(pageSize, 200);
        pageNumber = Math.Max(1, pageNumber);
        var result = await nodeService.GetNodesPagedAsync(pageNumber, pageSize, ct);
        var totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize);
        return Ok(new PagedResponse<NodeDto>(result.Items, result.TotalCount, result.Page, result.PageSize, totalPages));
    }

    [HttpGet("cursor")]
    [Authorize]
    [ProducesResponseType(typeof(CursorPageResult<NodeDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetNodesCursor(
        [FromQuery] string? cursor       = null,
        [FromQuery] int     pageSize     = 50,
        [FromQuery] bool    includeTotal = false,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);

        try
        {
            var result = await nodeService.GetNodesCursorAsync(
                new NodeCursorFilter
                {
                    Cursor       = cursor,
                    PageSize     = pageSize,
                    IncludeTotal = includeTotal,
                }, ct);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "InvalidCursorToken", message = ex.Message });
        }
    }

    [HttpGet("{nodeId}")]
    [Authorize]
    [ProducesResponseType(typeof(NodeDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetNode(string nodeId, CancellationToken ct)
    {
        var result = await nodeService.GetNodeAsync(nodeId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("{nodeId}/security")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(NodeSecurityInfoDto), 200)]
    public async Task<IActionResult> GetNodeSecurity(string nodeId, CancellationToken ct)
    {
        var result = await nodeService.GetNodeSecurityInfoAsync(nodeId, ct);
        return Ok(result);
    }

    [HttpGet("groups")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<NodeGroupDto>), 200)]
    public async Task<IActionResult> GetNodeGroups(CancellationToken ct)
    {
        var result = await nodeService.GetNodeGroupsAsync(ct);
        return Ok(result);
    }

    [HttpPut("{nodeId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(NodeDto), 200)]
    public async Task<IActionResult> UpdateNode(
        string nodeId, [FromBody] UpdateNodeRequest req, CancellationToken ct)
    {
        var result = await nodeService.UpdateNodeAsync(nodeId, req, ct);
        return Ok(result);
    }

    [HttpGet("registrations/pending")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<RegistrationRequestDto>), 200)]
    public async Task<IActionResult> GetPendingRegistrations(CancellationToken ct)
    {
        var result = await nodeService.GetPendingRegistrationsAsync(ct);
        return Ok(result);
    }

    [HttpDelete("registrations/{requestId:long}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RejectRegistration(long requestId, CancellationToken ct)
    {
        await nodeService.RejectRegistrationAsync(requestId, ct);
        return NoContent();
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(CreateNodeResult), 201)]
    public async Task<IActionResult> CreateNode([FromBody] CreateNodeRequest req, CancellationToken ct)
    {
        var result = await nodeService.CreateNodeAsync(req, ct);
        return CreatedAtAction(nameof(GetNode), new { nodeId = result.NodeId }, result);
    }

    [HttpPost("activate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ActivateResultDto), 200)]
    public async Task<ActionResult<ActivateResultDto>> Activate(
        [FromBody] ActivateRequest request, CancellationToken ct)
    {
        var result = await lifecycleService.ActivateAsync(
            request.ExternalId, request.BootstrapToken, request.AgentVersion, ct);
        return Ok(result);
    }

    [HttpPost("{nodeId}/heartbeat")]
    [Authorize(Policy = "NodeToken")]
    [ProducesResponseType(typeof(HeartbeatResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(410)]
    public async Task<IActionResult> Heartbeat(
        string nodeId, [FromBody] HeartbeatRequest request, CancellationToken ct)
    {
        // All three must match: route, body, claim
        var claimNodeId = User.FindFirstValue("nodeId");
        if (!string.Equals(nodeId, request.NodeId, StringComparison.Ordinal)
            || !string.Equals(nodeId, claimNodeId, StringComparison.Ordinal))
        {
            return BadRequest(new ErrorResponse("NodeId mismatch between route, body, and token claim"));
        }

        var node = await nodeService.GetNodeAsync(nodeId, ct);
        if (node == null) return NotFound();

        // Lifecycle accept/reject matrix (spec §5.3). No lifecycle write anywhere in this action.
        switch (node.LifecycleState)
        {
            case NodeLifecycleState.Active:
            case NodeLifecycleState.Recovery:
            case NodeLifecycleState.Decommissioning:
            case NodeLifecycleState.Draining:
                break;   // accepted — draining/recovering nodes still report telemetry
            case NodeLifecycleState.PendingRegistration:
            case NodeLifecycleState.PendingApproval:
                return Forbid();                    // 403 — activation is the readiness proof
            case NodeLifecycleState.Disabled:
                return Forbid();                    // 403
            case NodeLifecycleState.Decommissioned:
            case NodeLifecycleState.Rejected:
                return StatusCode(StatusCodes.Status410Gone);   // agent should stop
            default:
                return Forbid();
        }

        await nodeService.RecordHeartbeatAsync(nodeId, clock.UtcNow, ct);
        var response = await heartbeatProcessor.ProcessAsync(nodeId, request, ct);
        return Ok(response);
    }
}
