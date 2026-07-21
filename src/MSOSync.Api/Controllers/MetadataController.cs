using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Metadata;
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
    [ProducesResponseType(typeof(MetadataSummaryResponse), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var nodesTask      = nodes.GetNodesAsync(ct);
        var triggersTask   = triggers.GetTriggersAsync(ct);
        var routersTask    = routers.GetRoutersAsync(ct);
        var channelsTask   = channels.GetChannelsAsync(ct);
        var parametersTask = parameters.GetParametersAsync(null, ct);

        await Task.WhenAll(nodesTask, triggersTask, routersTask, channelsTask, parametersTask);

        return Ok(new MetadataSummaryResponse(
            nodesTask.Result.Count,
            triggersTask.Result.Count,
            routersTask.Result.Count,
            channelsTask.Result.Count,
            parametersTask.Result.Count));
    }
}
