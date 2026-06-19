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
        var nodesTask      = nodes.GetNodesAsync(ct);
        var triggersTask   = triggers.GetTriggersAsync(ct);
        var routersTask    = routers.GetRoutersAsync(ct);
        var channelsTask   = channels.GetChannelsAsync(ct);
        var parametersTask = parameters.GetParametersAsync(ct);

        await Task.WhenAll(nodesTask, triggersTask, routersTask, channelsTask, parametersTask);

        return Ok(new
        {
            nodes      = nodesTask.Result.Count,
            triggers   = triggersTask.Result.Count,
            routers    = routersTask.Result.Count,
            channels   = channelsTask.Result.Count,
            parameters = parametersTask.Result.Count
        });
    }
}
