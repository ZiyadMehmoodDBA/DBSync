using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MSOSync.Api.Auth;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService apiKeyService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ExtractKey(Request);
        if (string.IsNullOrEmpty(apiKey)) return AuthenticateResult.NoResult();

        // Try user API key first
        var user = await apiKeyService.ValidateUserKeyAsync(apiKey, Context.RequestAborted);
        if (user is not null)
        {
            var claims = new List<Claim>
            {
                // Use "userId" to match the claim name emitted by JwtService so that
                // controllers (e.g. ApiKeyController, MfaController) work under both schemes.
                new("userId", user.UserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("auth_method", "api_key"),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        // Try service account key
        var account = await apiKeyService.ValidateServiceAccountKeyAsync(apiKey, Context.RequestAborted);
        if (account is not null)
        {
            var claims = new List<Claim>
            {
                // Service accounts use NameIdentifier with "sa_" prefix (not a regular user ID).
                new(ClaimTypes.NameIdentifier, $"sa_{account.Id}"),
                new(ClaimTypes.Name, account.Name),
                new("auth_method", "service_account"),
            };
            if (account.PermissionsJson is not null)
                claims.Add(new Claim("permissions", account.PermissionsJson));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        return AuthenticateResult.Fail("Invalid API key");
    }

    private static string? ExtractKey(HttpRequest request)
    {
        // Check X-Api-Key header first
        if (request.Headers.TryGetValue("X-Api-Key", out var headerVal))
            return headerVal.ToString();

        // Check Authorization: ApiKey <key>
        if (request.Headers.TryGetValue("Authorization", out var authVal))
        {
            var auth = authVal.ToString();
            if (auth.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                return auth["ApiKey ".Length..].Trim();
        }

        return null;
    }
}
