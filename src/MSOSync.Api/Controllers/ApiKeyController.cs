using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Auth;
using System.Security.Claims;

namespace MSOSync.Api.Controllers;

public sealed record CreateApiKeyRequest(string Name, DateTime? ExpiresAt);

[ApiController]
[Route("api/v1/api-keys")]
[Authorize]
public sealed class ApiKeyController(IApiKeyService apiKeyService) : ControllerBase
{
    private long CurrentUserId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User not authenticated"));

    [HttpPost]
    public async Task<ActionResult<object>> CreateKey(
        [FromBody] CreateApiKeyRequest request, CancellationToken ct = default)
    {
        var (rawKey, entity) = await apiKeyService.CreateUserKeyAsync(
            CurrentUserId, request.Name, request.ExpiresAt, ct);

        return Ok(new
        {
            id         = entity.Id,
            name       = entity.Name,
            key        = rawKey,   // only returned once
            prefix     = entity.KeyPrefix,
            created_at = entity.CreatedAt,
            expires_at = entity.ExpiresAt,
        });
    }

    [HttpGet]
    public IActionResult ListKeys()
    {
        // Placeholder: returns empty list; full implementation requires repository query
        return Ok(Array.Empty<object>());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RevokeKey(int id, CancellationToken ct = default)
    {
        await apiKeyService.RevokeUserKeyAsync(id, ct);
        return NoContent();
    }
}
