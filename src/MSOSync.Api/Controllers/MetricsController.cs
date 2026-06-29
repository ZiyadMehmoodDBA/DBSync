using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Metrics;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/metrics")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class MetricsController(IMetricsQueryService metrics) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await metrics.GetSummaryAsync(ct));

    [HttpGet("nodes")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
        => Ok(await metrics.GetNodeMetricsAsync(ct));

    [HttpGet("channels")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetChannels(CancellationToken ct)
        => Ok(await metrics.GetChannelMetricsAsync(ct));

    [HttpGet("runtime")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRuntime(CancellationToken ct)
        => Ok(await metrics.GetRuntimeMetricsAsync(ct));

    [HttpGet("monitors")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetMonitors(
        [FromQuery] string? nodeId,
        [FromQuery] string? metricName,
        CancellationToken ct)
        => Ok(await metrics.GetMonitorMetricsAsync(nodeId, metricName, ct));
}
