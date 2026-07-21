using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/channels")]
public sealed class ChannelsController(IChannelMetadataService channelService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelDto>), 200)]
    public async Task<IActionResult> GetChannels(CancellationToken ct)
    {
        var result = await channelService.GetChannelsAsync(ct);
        return Ok(result);
    }

    [HttpGet("{channelId}")]
    [Authorize]
    [ProducesResponseType(typeof(ChannelDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetChannel(string channelId, CancellationToken ct)
    {
        var result = await channelService.GetChannelAsync(channelId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(ChannelDto), 201)]
    public async Task<IActionResult> CreateChannel([FromBody] CreateChannelRequest req, CancellationToken ct)
    {
        var result = await channelService.CreateChannelAsync(req, ct);
        return CreatedAtAction(nameof(GetChannel), new { channelId = result.ChannelId }, result);
    }

    [HttpPut("{channelId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(ChannelDto), 200)]
    public async Task<IActionResult> UpdateChannel(
        string channelId, [FromBody] UpdateChannelRequest req, CancellationToken ct)
    {
        var result = await channelService.UpdateChannelAsync(channelId, req, ct);
        return Ok(result);
    }

    [HttpDelete("{channelId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteChannel(string channelId, CancellationToken ct)
    {
        await channelService.DeleteChannelAsync(channelId, ct);
        return NoContent();
    }
}
