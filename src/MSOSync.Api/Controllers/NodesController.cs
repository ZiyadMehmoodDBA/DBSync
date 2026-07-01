using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Nodes;
using MSOSync.Common;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Nodes;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/nodes")]
public sealed class NodesController(
    INodeMetadataService nodeService,
    INodeStateMachine    stateMachine,
    IClock               clock) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
    {
        var result = await nodeService.GetNodesAsync(ct);
        return Ok(result);
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

    [HttpPost("{nodeId}/enable")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> EnableNode(string nodeId, CancellationToken ct)
    {
        await nodeService.EnableNodeAsync(nodeId, ct);
        return Ok();
    }

    [HttpPost("{nodeId}/disable")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> DisableNode(string nodeId, CancellationToken ct)
    {
        await nodeService.DisableNodeAsync(nodeId, ct);
        return Ok();
    }

    [HttpGet("registrations/pending")]
    [Authorize]
    public async Task<IActionResult> GetPendingRegistrations(CancellationToken ct)
    {
        var result = await nodeService.GetPendingRegistrationsAsync(ct);
        return Ok(result);
    }

    [HttpPost("registrations/{requestId:long}/approve")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> ApproveRegistration(long requestId, CancellationToken ct)
    {
        var result = await nodeService.ApproveRegistrationAsync(requestId, ct);
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
        if (node.Status == "DISABLED") return Forbid();

        // Update LastHeartbeat
        await nodeService.RecordHeartbeatAsync(nodeId, clock.UtcNow, ct);

        // Self-heal: OFFLINE → REGISTERED
        if (node.Status == "OFFLINE")
            await stateMachine.TransitionAsync(nodeId, "REGISTERED", ct);

        return NoContent();
    }
}
