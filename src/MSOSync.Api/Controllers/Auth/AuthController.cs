using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Dtos.Auth;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Api.Controllers.Auth;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    AuthenticationService authService,
    AppDbContext db,
    JwtService jwtService) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var correlationId = GetOrCreateCorrelationId();
        var result = await authService.LoginAsync(
            request.Username, request.Password, correlationId, ct);

        if (!result.Success)
            return Unauthorized(new { error = result.Error });

        // Multiple memberships — return picker list, client must call switch-tenant
        if (result.RequiresTenantSelection)
        {
            return StatusCode(300, new
            {
                requiresTenantSelection = true,
                refreshToken = result.RefreshToken,
                tenants = result.Tenants?.Select(t => new { t.TenantId, t.TenantSlug })
            });
        }

        return Ok(new LoginResponse(result.AccessToken!, result.RefreshToken!, result.ExpiresAt!.Value));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("RefreshPolicy")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        var correlationId = GetOrCreateCorrelationId();
        var result = await authService.RefreshAsync(request.RefreshToken, correlationId, ct);

        if (!result.Success)
            return Unauthorized(new { error = result.Error });

        return Ok(new RefreshResponse(result.AccessToken!, result.RefreshToken!, result.ExpiresAt!.Value));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        _ = long.TryParse(User.FindFirstValue("userId"), out var callerUserId);
        await authService.LogoutAsync(request.RefreshToken, callerUserId, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var username = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? string.Empty;

        var roles = User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        return Ok(new MeResponse(username, roles));
    }

    [HttpPost("switch-tenant")]
    [Authorize]
    public async Task<IActionResult> SwitchTenant(
        [FromBody] SwitchTenantRequest request,
        CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue("userId");
        if (!long.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        // Validate the new tenant membership
        var membership = await db.TenantMemberships
            .AsNoTracking()
            .Include(m => m.Tenant)
            .FirstOrDefaultAsync(m => m.TenantId == request.TenantId
                                   && m.UserId   == userId
                                   && m.Status   == MemberStatus.Active, ct);

        if (membership is null || membership.Tenant?.Status != TenantStatus.Active)
            return Forbid();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null) return Unauthorized();

        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.RoleId, (_, r) => r.RoleName)
            .ToListAsync(ct);

        var token = jwtService.CreateAccessToken(userId, user.Username, roles, tenantId: request.TenantId);
        return Ok(new { token, tenantId = request.TenantId, tenantSlug = membership.Tenant!.Slug });
    }

    private string GetOrCreateCorrelationId() =>
        Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
}

public sealed record SwitchTenantRequest(Guid TenantId);
