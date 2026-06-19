using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/routers")]
public sealed class RoutersController(IRouterMetadataService routerService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetRouters(CancellationToken ct)
    {
        var result = await routerService.GetRoutersAsync(ct);
        return Ok(result);
    }

    [HttpGet("{routerId}")]
    [Authorize]
    public async Task<IActionResult> GetRouter(string routerId, CancellationToken ct)
    {
        var result = await routerService.GetRouterAsync(routerId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("source/{groupId}")]
    [Authorize]
    public async Task<IActionResult> GetRoutersForSourceGroup(string groupId, CancellationToken ct)
    {
        var result = await routerService.GetRoutersForSourceGroupAsync(groupId, ct);
        return Ok(result);
    }

    [HttpGet("target/{groupId}")]
    [Authorize]
    public async Task<IActionResult> GetRoutersForTargetGroup(string groupId, CancellationToken ct)
    {
        var result = await routerService.GetRoutersForTargetGroupAsync(groupId, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> CreateRouter([FromBody] CreateRouterRequest req, CancellationToken ct)
    {
        var result = await routerService.CreateRouterAsync(req, ct);
        return CreatedAtAction(nameof(GetRouter), new { routerId = result.RouterId }, result);
    }

    [HttpPut("{routerId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> UpdateRouter(
        string routerId, [FromBody] UpdateRouterRequest req, CancellationToken ct)
    {
        var result = await routerService.UpdateRouterAsync(routerId, req, ct);
        return Ok(result);
    }

    [HttpDelete("{routerId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteRouter(string routerId, CancellationToken ct)
    {
        await routerService.DeleteRouterAsync(routerId, ct);
        return NoContent();
    }
}
