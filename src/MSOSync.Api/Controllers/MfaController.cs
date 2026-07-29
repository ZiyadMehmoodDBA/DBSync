using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Auth;
using MSOSync.Persistence;
using MSOSync.Security;
using System.Security.Claims;

namespace MSOSync.Api.Controllers;

public sealed record ConfirmEnrollRequest(string Code);
public sealed record MfaVerifyRequest(string MfaToken, string? TotpCode, string? BackupCode);

[ApiController]
[Route("api/v1/auth/mfa")]
public sealed class MfaController(
    IMfaService mfaService,
    MfaTokenService mfaTokenService,
    JwtService jwtService,
    IUserService userService,
    AppDbContext db) : ControllerBase
{
    private long CurrentUserId =>
        long.Parse(User.FindFirstValue("userId")
            ?? throw new InvalidOperationException("User not authenticated"));

    /// <summary>
    /// POST /api/v1/auth/mfa/enroll — start TOTP enrollment, returns secret + otpauth URI.
    /// </summary>
    [HttpPost("enroll")]
    [Authorize]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Enroll(CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        var secret = await mfaService.EnrollAsync(userId, ct);

        var issuer  = Uri.EscapeDataString("MSOSync");
        var account = Uri.EscapeDataString(User.Identity?.Name ?? userId.ToString());
        var totpUri = $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";

        return Ok(new { secret, totp_uri = totpUri });
    }

    /// <summary>
    /// POST /api/v1/auth/mfa/enroll/confirm — verify the first TOTP code; activates MFA and returns 8 raw backup codes.
    /// </summary>
    [HttpPost("enroll/confirm")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(object), 400)]
    public async Task<IActionResult> ConfirmEnroll(
        [FromBody] ConfirmEnrollRequest request,
        CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        try
        {
            await mfaService.ConfirmEnrollmentAsync(userId, request.Code, ct);
            var backupCodes = await mfaService.GenerateBackupCodesAsync(userId, ct);
            return Ok(new
            {
                backup_codes = backupCodes,
                message = "MFA enabled. Store backup codes securely — they will not be shown again."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/v1/auth/mfa/verify — verify TOTP or backup code via mfa_token; returns full access JWT.
    /// </summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Verify(
        [FromBody] MfaVerifyRequest request,
        CancellationToken ct = default)
    {
        var userId = mfaTokenService.Validate(request.MfaToken);
        if (userId is null) return Unauthorized();

        var codeToVerify = request.TotpCode ?? request.BackupCode;
        if (string.IsNullOrEmpty(codeToVerify))
            return BadRequest(new { error = "Provide totp_code or backup_code" });

        bool verified = request.TotpCode is not null
            ? await mfaService.VerifyTotpAsync(userId.Value, request.TotpCode, ct)
            : await mfaService.VerifyBackupCodeAsync(userId.Value, request.BackupCode!, ct);

        if (!verified) return Unauthorized();

        var user = await db.Users.FindAsync([userId.Value], ct);
        if (user is null) return Unauthorized();

        var roles = await userService.GetRolesAsync(userId.Value, ct);
        var token = jwtService.CreateAccessToken(user.UserId, user.Username, roles);
        return Ok(new { token });
    }

    /// <summary>
    /// DELETE /api/v1/auth/mfa/enroll — disable MFA for the current user (requires valid TOTP code).
    /// </summary>
    [HttpDelete("enroll")]
    [Authorize]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(object), 400)]
    public async Task<IActionResult> DisableMfa(
        [FromBody] ConfirmEnrollRequest request,
        CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (!await mfaService.VerifyTotpAsync(userId, request.Code, ct))
            return BadRequest(new { error = "Invalid TOTP code" });

        var old = await db.BackupCodes
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);
        db.BackupCodes.RemoveRange(old);

        var secret = await db.TotpSecrets.FindAsync([userId], ct);
        if (secret is not null)
            db.TotpSecrets.Remove(secret);

        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null)
            user.IsMfaEnabled = false;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
