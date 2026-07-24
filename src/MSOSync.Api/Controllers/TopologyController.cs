using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Topology;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/topology")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class TopologyController(ITopologyQueryService topology) : ControllerBase
{
    [HttpGet("graph")]
    [ProducesResponseType(typeof(TopologyGraphDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetGraph(
        [FromQuery] string? nodeIds = null,
        CancellationToken ct = default)
    {
        string[]? filter = null;
        if (!string.IsNullOrWhiteSpace(nodeIds))
        {
            filter = nodeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (filter.Length > 50)
                return BadRequest(new { error = "TooManyNodeIds", message = "Maximum 50 node IDs allowed in nodeIds filter." });
        }

        return Ok(await topology.GetTopologyGraphAsync(filter, ct));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(TopologySummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await topology.GetTopologySummaryAsync(ct));

    [HttpGet("groups")]
    [ProducesResponseType(typeof(IReadOnlyList<TopologyGroupDto>), 200)]
    public async Task<IActionResult> GetGroups(CancellationToken ct)
        => Ok(await topology.GetGroupsAsync(ct));

    [HttpGet("groups/{groupId}")]
    [ProducesResponseType(typeof(TopologyGroupDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetGroup(string groupId, CancellationToken ct)
    {
        var group = await topology.GetGroupAsync(groupId, ct);
        if (group is null) throw new NotFoundException($"Group {groupId} not found.");
        return Ok(group);
    }

    [HttpGet("groups/{groupId}/nodes")]
    [ProducesResponseType(typeof(CursorPageResult<TopologyGroupNodeDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetGroupNodes(
        string groupId,
        [FromQuery] string? cursor   = null,
        [FromQuery] int     pageSize = 100,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        try
        {
            return Ok(await topology.GetGroupNodesAsync(groupId, cursor, pageSize, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "InvalidCursorToken", message = ex.Message });
        }
    }
}
