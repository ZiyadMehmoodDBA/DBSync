using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Nodes;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Trigger;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/triggers")]
public sealed class TriggersController(
    ITriggerMetadataService triggerService,
    ITriggerInstallationService installationService,
    ITriggerDriftDetector driftDetector) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<TriggerDto>), 200)]
    public async Task<IActionResult> GetTriggers(CancellationToken ct)
    {
        var result = await triggerService.GetTriggersAsync(ct);
        return Ok(result);
    }

    [HttpGet("{triggerId}")]
    [Authorize]
    [ProducesResponseType(typeof(TriggerDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetTrigger(string triggerId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggerAsync(triggerId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("channel/{channelId}")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<TriggerDto>), 200)]
    public async Task<IActionResult> GetTriggersForChannel(string channelId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggersForChannelAsync(channelId, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(TriggerDto), 201)]
    public async Task<IActionResult> CreateTrigger([FromBody] CreateTriggerRequest req, CancellationToken ct)
    {
        var result = await triggerService.CreateTriggerAsync(req, ct);
        return CreatedAtAction(nameof(GetTrigger), new { triggerId = result.TriggerId }, result);
    }

    [HttpPut("{triggerId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(TriggerDto), 200)]
    public async Task<IActionResult> UpdateTrigger(
        string triggerId, [FromBody] UpdateTriggerRequest req, CancellationToken ct)
    {
        var result = await triggerService.UpdateTriggerAsync(triggerId, req, ct);
        return Ok(result);
    }

    [HttpDelete("{triggerId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteTrigger(string triggerId, CancellationToken ct)
    {
        await triggerService.DeleteTriggerAsync(triggerId, ct);
        return NoContent();
    }

    [HttpPost("{triggerId}/enable")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> EnableTrigger(string triggerId, CancellationToken ct)
    {
        await triggerService.EnableTriggerAsync(triggerId, ct);
        return Ok();
    }

    [HttpPost("{triggerId}/disable")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> DisableTrigger(string triggerId, CancellationToken ct)
    {
        await triggerService.DisableTriggerAsync(triggerId, ct);
        return Ok();
    }

    [HttpGet("{triggerId}/routers")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<TriggerRouterDto>), 200)]
    public async Task<IActionResult> GetTriggerRouters(string triggerId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggerRoutersAsync(triggerId, ct);
        return Ok(result);
    }

    [HttpPost("{triggerId}/routers")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> AddTriggerRouter(
        string triggerId, [FromBody] AddTriggerRouterRequest req, CancellationToken ct)
    {
        await triggerService.AddTriggerRouterAsync(triggerId, req.RouterId, ct);
        return Ok();
    }

    [HttpDelete("{triggerId}/routers/{routerId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RemoveTriggerRouter(
        string triggerId, string routerId, CancellationToken ct)
    {
        await triggerService.RemoveTriggerRouterAsync(triggerId, routerId, ct);
        return NoContent();
    }

    [HttpGet("{triggerId}/history")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<TriggerHistDto>), 200)]
    public async Task<IActionResult> GetTriggerHistory(string triggerId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggerHistoryAsync(triggerId, ct);
        return Ok(result);
    }

    [HttpPost("{triggerId}/rebuild")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(TriggerVerifyResult), 200)]
    public async Task<IActionResult> RebuildTrigger(string triggerId, CancellationToken ct)
    {
        var result = await installationService.RebuildAsync(triggerId, ct);
        return Ok(result);
    }

    [HttpPost("{triggerId}/verify")]
    [Authorize]
    [ProducesResponseType(typeof(TriggerVerifyResult), 200)]
    public async Task<IActionResult> VerifyTrigger(string triggerId, CancellationToken ct)
    {
        var result = await driftDetector.VerifyAsync(triggerId, ct);
        return Ok(result);
    }
}
