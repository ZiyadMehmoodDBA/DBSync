using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Metadata.Metrics;
using MSOSync.Metrics;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/metrics")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class MetricsController(
    IMetricsQueryService metrics,
    IOptions<TelemetryOptions> telemetryOptions,
    IConfiguration config) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(204)]
    public IActionResult GetTelemetryStatus()
    {
        var opts = telemetryOptions.Value;
        if (!opts.Enabled)
            return NoContent();

        var grafanaUrl = config["Observability:GrafanaUrl"];
        return Ok(new
        {
            message = "Telemetry is enabled. Metrics are available at /metrics (Prometheus format).",
            prometheusEndpoint = "/metrics",
            grafanaUrl = string.IsNullOrEmpty(grafanaUrl) ? null : grafanaUrl
        });
    }

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
