using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MSOSync.Security;

public sealed class JwtService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly SymmetricSecurityKey _key;

    public JwtService(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("MSOSYNC_JWT_SECRET")
            ?? throw new InvalidOperationException(
                "MSOSYNC_JWT_SECRET is required (set via env var or Jwt:Secret config key)");

        if (secret.Length < 32)
            throw new InvalidOperationException(
                "MSOSYNC_JWT_SECRET must be at least 32 characters");

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public string CreateAccessToken(long userId, string username, IEnumerable<string> roles)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new("userId", userId.ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now,
            expires: now.Add(AccessTokenLifetime),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.MapInboundClaims = false;
            return handler.ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _key,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = ClockSkew
                },
                out _);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
