using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Health;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/health")]
[Authorize(Policy = "AdminOnly")]
public sealed class HealthScoreController(IHealthScoringService scoringService) : ControllerBase
{
    [HttpGet("scores")]
    public async Task<ActionResult<IEnumerable<NodeHealthScore>>> GetScores(CancellationToken ct = default)
        => Ok(await scoringService.GetScoresAsync(ct));

    [HttpGet("scores/{nodeId}")]
    public async Task<ActionResult<NodeHealthScore>> GetScore(string nodeId, CancellationToken ct = default)
    {
        var score = await scoringService.GetScoreAsync(nodeId, ct);
        return score is null ? NotFound() : Ok(score);
    }
}
