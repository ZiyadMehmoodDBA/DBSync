using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Nodes;
using MSOSync.Common;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/nodes")]
public sealed class NodesController(
    INodeMetadataService nodeService,
    IClock               clock,
    INodeLifecycleService lifecycleService,
    HeartbeatProcessor heartbeatProcessor) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
    {
        var result = await nodeService.GetNodesAsync(ct);
        return Ok(result);
    }

    [HttpGet("paged")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<NodeDto>), 200)]
    public async Task<IActionResult> GetNodesPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 50,
        CancellationToken ct = default)
    {
        pageSize = Math.Min(pageSize, 200);
        pageNumber = Math.Max(1, pageNumber);
        var result = await nodeService.GetNodesPagedAsync(pageNumber, pageSize, ct);
        var totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize);
        return Ok(new { data = result.Items, total = result.TotalCount, page = result.Page, pageSize = result.PageSize, totalPages });
    }

    [HttpGet("{nodeId}")]
    [Authorize]
    public async Task<IActionResult> GetNode(string nodeId, CancellationToken ct)
    {
        var result = await nodeService.GetNodeAsync(nodeId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("{nodeId}/security")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetNodeSecurity(string nodeId, CancellationToken ct)
    {
        var result = await nodeService.GetNodeSecurityInfoAsync(nodeId, ct);
        return Ok(result);
    }

    [HttpGet("groups")]
    [Authorize]
    public async Task<IActionResult> GetNodeGroups(CancellationToken ct)
    {
        var result = await nodeService.GetNodeGroupsAsync(ct);
        return Ok(result);
    }

    [HttpPut("{nodeId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> UpdateNode(
        string nodeId, [FromBody] UpdateNodeRequest req, CancellationToken ct)
    {
        var result = await nodeService.UpdateNodeAsync(nodeId, req, ct);
        return Ok(result);
    }

    [HttpGet("registrations/pending")]
    [Authorize]
    public async Task<IActionResult> GetPendingRegistrations(CancellationToken ct)
    {
        var result = await nodeService.GetPendingRegistrationsAsync(ct);
        return Ok(result);
    }

    [HttpDelete("registrations/{requestId:long}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RejectRegistration(long requestId, CancellationToken ct)
    {
        await nodeService.RejectRegistrationAsync(requestId, ct);
        return NoContent();
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreateNode([FromBody] CreateNodeRequest req, CancellationToken ct)
    {
        var result = await nodeService.CreateNodeAsync(req, ct);
        return CreatedAtAction(nameof(GetNode), new { nodeId = result.NodeId }, result);
    }

    [HttpPost("activate")]
    [AllowAnonymous]
    public async Task<ActionResult<ActivateResultDto>> Activate(
        [FromBody] ActivateRequest request, CancellationToken ct)
    {
        var result = await lifecycleService.ActivateAsync(
            request.ExternalId, request.BootstrapToken, request.AgentVersion, ct);
        return Ok(result);
    }

    [HttpPost("{nodeId}/heartbeat")]
    [Authorize(Policy = "NodeToken")]
    public async Task<IActionResult> Heartbeat(
        string nodeId, [FromBody] HeartbeatRequest request, CancellationToken ct)
    {
        // All three must match: route, body, claim
        var claimNodeId = User.FindFirstValue("nodeId");
        if (!string.Equals(nodeId, request.NodeId, StringComparison.Ordinal)
            || !string.Equals(nodeId, claimNodeId, StringComparison.Ordinal))
        {
            return BadRequest(new { error = "NodeId mismatch between route, body, and token claim" });
        }

        var node = await nodeService.GetNodeAsync(nodeId, ct);
        if (node == null) return NotFound();

        // Lifecycle accept/reject matrix (spec §5.3). No lifecycle write anywhere in this action.
        switch (node.LifecycleState)
        {
            case NodeLifecycleState.Active:
            case NodeLifecycleState.Recovery:
            case NodeLifecycleState.Decommissioning:
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
