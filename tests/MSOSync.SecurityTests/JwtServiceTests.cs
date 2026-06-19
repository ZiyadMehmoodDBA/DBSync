using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MSOSync.Security;
using Xunit;

namespace MSOSync.SecurityTests;

public sealed class JwtServiceTests
{
    private static JwtService MakeService(string secret = "this-is-a-test-secret-at-least-32chars!!")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secret })
            .Build();
        return new JwtService(config);
    }

    [Fact]
    public void CreateAccessToken_ReturnsNonEmptyToken()
    {
        var svc = MakeService();
        var token = svc.CreateAccessToken(1L, "admin", ["ADMIN"]);
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsPrincipalWithClaims()
    {
        var svc = MakeService();
        var token = svc.CreateAccessToken(42L, "testuser", ["OPERATOR", "VIEWER"]);

        var principal = svc.ValidateToken(token);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.Role).Should().NotBeNull();
        principal.FindFirst(ClaimTypes.Role)!.Value.Should().Be("OPERATOR");
        principal.FindFirst("userId").Should().NotBeNull();
        principal.FindFirst("userId")!.Value.Should().Be("42");
        // Check for "sub" claim - JwtRegisteredClaimNames.Sub should be "sub"
        var subClaim = principal.FindFirst("sub");
        subClaim.Should().NotBeNull();
        subClaim!.Value.Should().Be("testuser");
    }

    [Fact]
    public void ValidateToken_WrongSecret_ReturnsNull()
    {
        var svc1 = MakeService("this-is-a-test-secret-at-least-32chars!!");
        var svc2 = MakeService("this-is-a-DIFFERENT-secret-at-32chars!!");
        var token = svc1.CreateAccessToken(1L, "admin", ["ADMIN"]);

        svc2.ValidateToken(token).Should().BeNull();
    }

    [Fact]
    public void ValidateToken_Garbage_ReturnsNull()
    {
        var svc = MakeService();
        svc.ValidateToken("not.a.jwt").Should().BeNull();
    }

    [Fact]
    public void Ctor_ShortSecret_ThrowsInvalidOperation()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "short" })
            .Build();
        var act = () => new JwtService(config);
        act.Should().Throw<InvalidOperationException>().WithMessage("*32 characters*");
    }

    [Fact]
    public void Ctor_MissingSecret_ThrowsInvalidOperation()
    {
        var saved = Environment.GetEnvironmentVariable("MSOSYNC_JWT_SECRET");
        Environment.SetEnvironmentVariable("MSOSYNC_JWT_SECRET", null);
        try
        {
            var config = new ConfigurationBuilder().Build();
            var act = () => new JwtService(config);
            act.Should().Throw<InvalidOperationException>().WithMessage("*MSOSYNC_JWT_SECRET*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MSOSYNC_JWT_SECRET", saved);
        }
    }
}
