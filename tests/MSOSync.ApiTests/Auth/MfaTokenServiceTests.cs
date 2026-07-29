using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MSOSync.Api.Auth;
using Xunit;

namespace MSOSync.ApiTests.Auth;

/// <summary>
/// Unit tests for MfaTokenService — token creation, validation, expiry, and tampering.
/// </summary>
public sealed class MfaTokenServiceTests
{
    private static MfaTokenService Build(string secret = "test-secret-that-is-at-least-32-chars!!")
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]   = secret,
                ["Jwt:Issuer"]   = "msosync",
                ["Jwt:Audience"] = "msosync-dashboard",
            })
            .Build();

        return new MfaTokenService(cfg);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ReturnsNonEmptyToken()
    {
        var svc   = Build();
        var token = svc.Create(userId: 42L);

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_TokenIsJwtFormat()
    {
        var svc   = Build();
        var token = svc.Create(userId: 1L);

        // JWT tokens have exactly three dot-separated base64url segments
        token.Split('.').Should().HaveCount(3);
    }

    // ── Validate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReturnsUserId_ForValidToken()
    {
        var svc   = Build();
        var token = svc.Create(userId: 99L);

        var result = svc.Validate(token);

        result.Should().Be(99L);
    }

    [Fact]
    public void Validate_ReturnsNull_ForGarbage()
    {
        var svc    = Build();
        var result = svc.Validate("not.a.jwt");

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_ReturnsNull_WhenSignedWithDifferentKey()
    {
        var issuer  = Build(secret: "secret-number-one-that-is-32-chars!!!");
        var checker = Build(secret: "secret-number-two-that-is-32-chars!!!");

        var token  = issuer.Create(userId: 7L);
        var result = checker.Validate(token);

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_ReturnsNull_ForEmptyString()
    {
        var svc    = Build();
        var result = svc.Validate(string.Empty);

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_ReturnsSameUserId_ForMultipleDifferentIds()
    {
        var svc = Build();

        foreach (var id in new long[] { 1, 42, 999, long.MaxValue })
        {
            var token  = svc.Create(id);
            var result = svc.Validate(token);
            result.Should().Be(id, because: $"userId {id} should round-trip");
        }
    }
}
