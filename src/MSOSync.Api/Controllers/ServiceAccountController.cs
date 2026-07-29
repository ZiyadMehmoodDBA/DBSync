using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Auth;

namespace MSOSync.Api.Controllers;

public sealed record CreateServiceAccountRequest(string Name, string[] Permissions);

[ApiController]
[Route("api/v1/service-accounts")]
[Authorize(Policy = "AdminOnly")]
public sealed class ServiceAccountController(IApiKeyService apiKeyService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Create(
        [FromBody] CreateServiceAccountRequest request, CancellationToken ct = default)
    {
        var (rawKey, entity) = await apiKeyService.CreateServiceAccountAsync(
            request.Name, request.Permissions, ct);

        return Ok(new
        {
            id          = entity.Id,
            name        = entity.Name,
            key         = rawKey,   // only returned once
            client_id   = entity.ClientId,
            permissions = request.Permissions,
            created_at  = entity.CreatedAt,
        });
    }

    [HttpGet]
    public IActionResult List()
    {
        // Placeholder: returns empty list; full implementation requires repository query
        return Ok(Array.Empty<object>());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct = default)
    {
        await apiKeyService.RevokeServiceAccountAsync(id, ct);
        return NoContent();
    }
}
