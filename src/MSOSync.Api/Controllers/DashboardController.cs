using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Dashboard;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class DashboardController(
    IDashboardQueryService        dashboard,
    IValidator<ActivityFilter>    activityValidator) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        return Ok(await dashboard.GetSummaryAsync(ct));
    }

    [HttpGet("activity")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetActivity(
        [FromQuery] ActivityFilter filter, CancellationToken ct)
    {
        await activityValidator.ValidateAndThrowAsync(filter, ct);
        return Ok(await dashboard.GetActivityAsync(filter, ct));
    }
}
