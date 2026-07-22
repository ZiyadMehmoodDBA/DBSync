using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Cluster;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Cluster.Dtos;
using MSOSync.Metadata.Operations.Cluster.HealthTrends;
using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ClusterController(
    IClusterSummaryQueryService        summary,
    IClusterHealthTrendService         healthTrends,
    IValidator<GetHealthTrendsRequest> healthTrendsValidator) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ClusterSummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await summary.GetSummaryAsync(ct));

    [HttpGet("health-trends")]
    [ProducesResponseType(typeof(ClusterHealthTrendDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetHealthTrends([FromQuery] GetHealthTrendsRequest req, CancellationToken ct)
    {
        await healthTrendsValidator.ValidateAndThrowAsync(req, ct);
        return Ok(await healthTrends.GetTrendsAsync(req.Window, req.NodeId, ct));
    }
}
