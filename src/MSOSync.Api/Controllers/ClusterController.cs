using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Cluster;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Cluster.Dtos;
using MSOSync.Metadata.Operations.Cluster.Diagnostics;
using MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;
using MSOSync.Metadata.Operations.Cluster.HealthTrends;
using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;
using MSOSync.Metadata.Operations.Cluster.Recovery;
using MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ClusterController(
    IClusterSummaryQueryService        summary,
    IClusterHealthTrendService         healthTrends,
    IValidator<GetHealthTrendsRequest> healthTrendsValidator,
    IRecoveryDashboardQueryService     recovery,
    IClusterDiagnosticsQueryService    diagnostics) : ControllerBase
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

    [HttpGet("recovery")]
    [ProducesResponseType(typeof(RecoveryDashboardDto), 200)]
    public async Task<IActionResult> GetRecovery(CancellationToken ct)
        => Ok(await recovery.GetRecoveryDashboardAsync(ct));

    [HttpGet("diagnostics")]
    [ProducesResponseType(typeof(ClusterDiagnosticsDto), 200)]
    public async Task<IActionResult> GetDiagnostics(CancellationToken ct)
        => Ok(await diagnostics.GetDiagnosticsAsync(ct));
}
