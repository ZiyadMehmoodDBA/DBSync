using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Cluster.Dtos;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ClusterController(IClusterSummaryQueryService svc) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ClusterSummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await svc.GetSummaryAsync(ct));
}
