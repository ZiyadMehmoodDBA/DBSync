// src/MSOSync.Api/Controllers/PreferencesController.cs
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Validators;
using MSOSync.Metadata.Preferences;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/preferences")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class PreferencesController(
    IUserPreferencesService          preferencesService,
    UpsertPreferenceRequestValidator keyValidator)
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
        await keyValidator.ValidateAndThrowAsync(key, ct);
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
