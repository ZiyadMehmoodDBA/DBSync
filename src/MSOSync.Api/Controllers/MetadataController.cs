using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/metadata")]
public sealed class MetadataController(
    INodeMetadataService nodes,
    ITriggerMetadataService triggers,
    IRouterMetadataService routers,
    IChannelMetadataService channels,
    IParameterMetadataService parameters) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var allNodes      = await nodes.GetNodesAsync(ct);
        var allTriggers   = await triggers.GetTriggersAsync(ct);
        var allRouters    = await routers.GetRoutersAsync(ct);
        var allChannels   = await channels.GetChannelsAsync(ct);
        var allParameters = await parameters.GetParametersAsync(ct);

        return Ok(new
        {
            nodes      = allNodes.Count,
            triggers   = allTriggers.Count,
            routers    = allRouters.Count,
            channels   = allChannels.Count,
            parameters = allParameters.Count
        });
    }
}
