using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Health;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/slo")]
[Authorize(Policy = "AdminOnly")]
public sealed class SloController(ISloService sloService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<SloStatus>> GetStatus(CancellationToken ct = default)
        => Ok(await sloService.GetStatusAsync(ct));
}
