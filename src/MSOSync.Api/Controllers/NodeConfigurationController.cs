using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Configuration;

namespace MSOSync.Api.Controllers;

/// <summary>
/// Node-facing configuration endpoint. Authenticated with node token (not user JWT).
/// Route: /api/v1/configurations — PLURAL (distinct from management /api/v1/configuration)
/// </summary>
[ApiController]
[Route("api/v1/configurations")]
[Authorize(Policy = "NodeToken")]
public sealed class NodeConfigurationController(INodeConfigurationService configSvc) : ControllerBase
{
    /// <summary>
    /// Get effective configuration for the calling node.
    /// Returns 200 with effective settings, 204 if no template assigned, 304 if ETag matches.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        // Node ID comes from the authenticated claims set by NodeTokenAuthMiddleware
        var nodeId = User.FindFirst("nodeId")?.Value;
        if (string.IsNullOrEmpty(nodeId))
            return Forbid();

        var ifNoneMatch = Request.Headers["If-None-Match"].FirstOrDefault();
        var result = await configSvc.GetCurrentAsync(nodeId, ifNoneMatch, ct);

        if (result.Config is null && !result.NotModified)
            return NoContent();  // 204 — no template assigned

        if (result.NotModified)
        {
            if (!string.IsNullOrEmpty(result.ETag))
                Response.Headers.ETag = $"\"{result.ETag}\"";
            return StatusCode(304);  // 304 Not Modified
        }

        if (!string.IsNullOrEmpty(result.ETag))
            Response.Headers.ETag = $"\"{result.ETag}\"";

        return Ok(result.Config);
    }
}
