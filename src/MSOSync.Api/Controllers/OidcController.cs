using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

public sealed record OidcConfigurationDto(
    string Name,
    string Authority,
    string ClientId,
    string ClientSecretKey,
    string Scopes = "openid profile email",
    string CallbackPath = "/auth/oidc/callback",
    bool IsEnabled = true);

[ApiController]
public sealed class OidcController(AppDbContext db) : ControllerBase
{
    [HttpGet("api/oidc/configurations")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IEnumerable<OidcConfigurationDto>>> GetConfigurations()
    {
        var configs = await db.OidcConfigurations
            .Select(c => new OidcConfigurationDto(
                c.Name, c.Authority, c.ClientId, c.ClientSecretKey,
                c.Scopes, c.CallbackPath, c.IsEnabled))
            .ToListAsync();
        return configs;
    }

    [HttpPost("api/oidc/configurations")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<OidcConfigurationDto>> CreateConfiguration(OidcConfigurationDto dto)
    {
        var entity = new OidcConfiguration
        {
            Name = dto.Name,
            Authority = dto.Authority,
            ClientId = dto.ClientId,
            ClientSecretKey = dto.ClientSecretKey,
            Scopes = dto.Scopes,
            CallbackPath = dto.CallbackPath,
            IsEnabled = dto.IsEnabled,
            CreatedAt = DateTime.UtcNow,
        };
        db.OidcConfigurations.Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetConfigurations), null, dto);
    }

    [HttpPut("api/oidc/configurations/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateConfiguration(int id, OidcConfigurationDto dto)
    {
        var entity = await db.OidcConfigurations.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Name = dto.Name;
        entity.Authority = dto.Authority;
        entity.ClientId = dto.ClientId;
        entity.ClientSecretKey = dto.ClientSecretKey;
        entity.Scopes = dto.Scopes;
        entity.CallbackPath = dto.CallbackPath;
        entity.IsEnabled = dto.IsEnabled;

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("api/oidc/configurations/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteConfiguration(int id)
    {
        var entity = await db.OidcConfigurations.FindAsync(id);
        if (entity is null) return NotFound();

        db.OidcConfigurations.Remove(entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("/auth/oidc/login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl = null)
        => Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
            OpenIdConnectDefaults.AuthenticationScheme);
}
