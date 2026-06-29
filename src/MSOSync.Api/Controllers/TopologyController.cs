using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Topology;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/topology")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class TopologyController(ITopologyQueryService topology) : ControllerBase
{
    [HttpGet("graph")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGraph(CancellationToken ct)
        => Ok(await topology.GetTopologyGraphAsync(ct));

    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await topology.GetTopologySummaryAsync(ct));

    [HttpGet("groups")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGroups(CancellationToken ct)
        => Ok(await topology.GetGroupsAsync(ct));

    [HttpGet("groups/{groupId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetGroup(string groupId, CancellationToken ct)
    {
        var group = await topology.GetGroupAsync(groupId, ct);
        if (group is null) throw new NotFoundException($"Group {groupId} not found.");
        return Ok(group);
    }

    [HttpGet("groups/{groupId}/nodes")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGroupNodes(string groupId, CancellationToken ct)
        => Ok(await topology.GetGroupNodesAsync(groupId, ct));
}
