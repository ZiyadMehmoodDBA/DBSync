// tests/MSOSync.IntegrationTests/Security/SecurityTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace MSOSync.IntegrationTests.Security;

[Collection("Security")]
public sealed class SecurityTests(SecurityFixture factory)
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private HttpClient Client() => factory.CreateClient();

    private async Task<(string Token, string RefreshToken)> LoginAdminAsync()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = factory.AdminUsername,
            Password = factory.AdminPassword
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponseBody>();
        return (body!.Token, body.RefreshToken);
    }

    // ── login ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = factory.AdminUsername,
            Password = factory.AdminPassword
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponseBody>();
        body!.Token.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = factory.AdminUsername,
            Password = "wrongpassword"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = "nosuchuser",
            Password = "anypassword"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_EmptyCredentials_Returns400()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = "",
            Password = ""
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── me ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithValidToken_Returns200WithUsername()
    {
        var (token, _) = await LoginAdminAsync();
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/v1/auth/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<MeResponseBody>();
        body!.Username.Should().Be(factory.AdminUsername);
        body.Roles.Should().Contain("ADMIN");
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var client = Client();
        var resp = await client.GetAsync("/api/v1/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── refresh ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ReturnsNewTokenPair()
    {
        var (_, refreshToken) = await LoginAdminAsync();
        var client = Client();

        var resp = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            RefreshToken = refreshToken
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponseBody>();
        body!.Token.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBe(refreshToken, "rotation must issue a new token");
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            RefreshToken = "not-a-real-token"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReuseDetection_RevokesFamily()
    {
        // Login → get refresh token R1
        var (_, r1) = await LoginAdminAsync();
        var client = Client();

        // Use R1 → get R2 (R1 is now consumed)
        var resp1 = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = r1 });
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-use R1 → should detect reuse, revoke family → 401
        var resp2 = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = r1 });
        resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── logout ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_WithValidToken_Returns204()
    {
        var (token, refreshToken) = await LoginAdminAsync();
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync("/api/v1/auth/logout", new
        {
            RefreshToken = refreshToken
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_RefreshTokenIsInvalidAfterLogout()
    {
        var (token, refreshToken) = await LoginAdminAsync();
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await client.PostAsJsonAsync("/api/v1/auth/logout", new { RefreshToken = refreshToken });

        var refreshResp = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            RefreshToken = refreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── security headers ──────────────────────────────────────────────────

    [Fact]
    public async Task SecurityHeaders_PresentOnAllResponses()
    {
        var client = Client();
        var resp = await client.GetAsync("/health");

        resp.Headers.Should().ContainKey("X-Content-Type-Options");
        resp.Headers.Should().ContainKey("X-Frame-Options");
        resp.Headers.Should().ContainKey("Referrer-Policy");
        resp.Headers.Should().ContainKey("Content-Security-Policy");
        resp.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        resp.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }

    // ── node token ────────────────────────────────────────────────────────

    [Fact]
    public async Task NodeToken_MissingHeaders_Returns401OnSyncPath()
    {
        var client = Client();
        var resp = await client.GetAsync("/api/v1/sync/status");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── rate limiting ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_RateLimit_Returns429()
    {
        // Use a unique X-Forwarded-For header so this test uses its own isolated rate-limit
        // bucket, independent of the shared "unknown" bucket used by other tests.
        // LoginPermitLimit = 50; send 51 requests with this specific IP to trigger 429.
        // The user "ratelimit-x" does not exist so each lookup is fast (no BCrypt).
        var client = Client();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.255.1");

        HttpStatusCode lastStatus = HttpStatusCode.Unauthorized;
        for (var i = 0; i < 60; i++)
        {
            var r = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = "ratelimit-x", Password = "y" });
            lastStatus = r.StatusCode;
            if (lastStatus == HttpStatusCode.TooManyRequests) break;
        }

        lastStatus.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_TokenHasIssuerAndAudience()
    {
        var (token, _) = await LoginAdminAsync();

        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(token);

        parsed.Issuer.Should().Be("msosync");
        parsed.Audiences.Should().Contain("msosync-dashboard");
    }

    [Fact]
    public async Task Refresh_UsesHashLookup_ReturnsNewToken()
    {
        var (_, refreshToken) = await LoginAdminAsync();

        var resp = await Client().PostAsJsonAsync("/api/v1/auth/refresh",
            new { RefreshToken = refreshToken });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<RefreshResponseBody>();
        body!.RefreshToken.Should().NotBe(refreshToken);
    }

    // ── DTO helpers ───────────────────────────────────────────────────────

    private sealed record LoginResponseBody(string Token, string RefreshToken, DateTime ExpiresAt);
    private sealed record RefreshResponseBody(string Token, string RefreshToken, DateTime ExpiresAt);
    private sealed record MeResponseBody(string Username, List<string> Roles);
}
