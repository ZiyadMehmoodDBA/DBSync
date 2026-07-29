using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MSOSync.Api.Auth;

/// <summary>
/// Issues and validates short-lived JWT tokens used during the MFA challenge step.
/// An mfa_token is issued after password verification when the user has TOTP enabled.
/// It carries only the user-id claim (no roles, no tenant) and expires in 5 minutes.
/// </summary>
public sealed class MfaTokenService(IConfiguration config)
{
    private const int ExpiryMinutes = 5;
    private const string MfaUserIdClaim = "mfa_user_id";

    public string Create(long userId)
    {
        var secret = config["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("MSOSYNC_JWT_SECRET")
            ?? throw new InvalidOperationException("Jwt:Secret not configured");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             config["Jwt:Issuer"]   ?? "msosync",
            audience:           config["Jwt:Audience"] ?? "msosync-dashboard",
            claims:             [new Claim(MfaUserIdClaim, userId.ToString())],
            expires:            DateTime.UtcNow.AddMinutes(ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public long? Validate(string mfaToken)
    {
        var secret = config["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("MSOSYNC_JWT_SECRET")
            ?? throw new InvalidOperationException("Jwt:Secret not configured");

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(mfaToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidateIssuer           = true,
                ValidIssuer              = config["Jwt:Issuer"] ?? "msosync",
                ValidateAudience         = true,
                ValidAudience            = config["Jwt:Audience"] ?? "msosync-dashboard",
                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.Zero,
            }, out _);

            var claim = principal.FindFirstValue(MfaUserIdClaim);
            return claim is null ? null : long.Parse(claim);
        }
        catch
        {
            return null;
        }
    }
}
