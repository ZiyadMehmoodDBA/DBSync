using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Health;
using MSOSync.Common.Workers;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class SystemController(
    ISystemHealthService healthSvc,
    IWorkerStatusRegistry workerRegistry) : ControllerBase
{
    [HttpGet("health")]
    [ProducesResponseType<HealthContribution[]>(200)]
    public async Task<IActionResult> GetHealthAsync(CancellationToken ct)
        => Ok(await healthSvc.GetAllAsync(ct));

    [HttpGet("workers")]
    [ProducesResponseType<WorkerStatusDto[]>(200)]
    public IActionResult GetWorkers()
        => Ok(workerRegistry.GetAll());
}
