// src/MSOSync.Api/Controllers/PreferencesController.cs
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Preferences;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/preferences")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class PreferencesController(IUserPreferencesService preferencesService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, JsonElement>), 200)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await preferencesService.GetAllAsync(ct));

    [HttpPut("{key}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Upsert(string key, [FromBody] JsonElement value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            return BadRequest(new { code = "INVALID_KEY", message = "Preference key must be 1–100 characters." });
        await preferencesService.UpsertAsync(key, value, ct);
        return Ok();
    }

    [HttpPut]
    [ProducesResponseType(200)]
    public async Task<IActionResult> BulkUpsert(
        [FromBody] Dictionary<string, JsonElement> preferences,
        CancellationToken ct)
    {
        await preferencesService.BulkUpsertAsync(preferences, ct);
        return Ok();
    }

    [HttpDelete("{key}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        await preferencesService.DeleteAsync(key, ct);
        return NoContent();
    }
}
