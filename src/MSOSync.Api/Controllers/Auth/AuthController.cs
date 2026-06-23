using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MSOSync.Api.Dtos.Auth;
using MSOSync.Security;

namespace MSOSync.Api.Controllers.Auth;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthenticationService authService) : ControllerBase
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

    private string GetOrCreateCorrelationId() =>
        Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
}
