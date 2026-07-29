using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MSOSync.Api.Auth;
using MSOSync.Api.Dtos.Auth;
using MSOSync.Api.Dtos.Common;
using MSOSync.Security;

namespace MSOSync.Api.Controllers.Auth;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    AuthenticationService authService,
    ITenantMembershipQueryService membershipQuery,
    JwtService jwtService,
    MfaTokenService mfaTokenService) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("LoginPolicy")]
    [ProducesResponseType(typeof(LoginResponse), 200)]
    [ProducesResponseType(typeof(MfaChallengeResponse), 202)]
    [ProducesResponseType(typeof(TenantSelectionResponse), 300)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var correlationId = GetOrCreateCorrelationId();
        var result = await authService.LoginAsync(
            request.Username, request.Password, correlationId, ct);

        if (!result.Success)
            return Unauthorized(new ErrorResponse(result.Error!));

        // MFA challenge: password is valid but a TOTP code is required before full token issuance.
        if (result.RequiresMfa)
        {
            var mfaToken = mfaTokenService.Create(result.UserId!.Value);
            return Accepted(new MfaChallengeResponse(RequiresMfa: true, MfaToken: mfaToken));
        }

        // Multiple memberships — return picker list, client must call switch-tenant
        if (result.RequiresTenantSelection)
        {
            return StatusCode(300, new TenantSelectionResponse(
                RequiresTenantSelection: true,
                RefreshToken: result.RefreshToken,
                Tenants: result.Tenants));
        }

        return Ok(new LoginResponse(result.AccessToken!, result.RefreshToken!, result.ExpiresAt!.Value));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("RefreshPolicy")]
    [ProducesResponseType(typeof(RefreshResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        var correlationId = GetOrCreateCorrelationId();
        var result = await authService.RefreshAsync(request.RefreshToken, correlationId, ct);

        if (!result.Success)
            return Unauthorized(new ErrorResponse(result.Error!));

        return Ok(new RefreshResponse(result.AccessToken!, result.RefreshToken!, result.ExpiresAt!.Value));
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(204)]
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
    [ProducesResponseType(typeof(MeResponse), 200)]
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
    [ProducesResponseType(typeof(SwitchTenantResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> SwitchTenant(
        [FromBody] SwitchTenantRequest request,
        CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue("userId");
        if (!long.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var context = await membershipQuery.GetSwitchTenantContextAsync(userId, request.TenantId, ct);
        if (context is null) return Forbid();

        var token = jwtService.CreateAccessToken(userId, context.Username, context.Roles, tenantId: request.TenantId);
        return Ok(new SwitchTenantResponse(token, request.TenantId, context.TenantSlug));
    }

    private string GetOrCreateCorrelationId() =>
        Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
}
