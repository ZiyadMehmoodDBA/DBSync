// src/MSOSync.Api/Controllers/NodeScopeController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.NodeManagement;
using System.Security.Claims;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/nodes/{nodeId}/scope")]
public sealed class NodeScopeController(INodeScopeService scopeService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(NodeScopeDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetScope(string nodeId, CancellationToken ct)
    {
        var result = await scopeService.GetScopeAsync(nodeId, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPut]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(NodeScopeDto), 200)]
    public async Task<IActionResult> SetScope(
        string nodeId, [FromBody] SetNodeScopeRequest req, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
        var result = await scopeService.SetScopeAsync(nodeId, req, actor, ct);
        return Ok(result);
    }
}
