using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Auth;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.AppTests.Auth;

public sealed class OidcUserProvisioningServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public OidcUserProvisioningServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static ClaimsPrincipal MakePrincipal(string sub, string email, string name) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim("sub", sub),
            new Claim("email", email),
            new Claim("name", name),
        }, "oidc"));

    [Fact]
    public async Task ProvisionAsync_CreatesUser_WhenNotExists()
    {
        var svc = new OidcUserProvisioningService(_db);
        var principal = MakePrincipal("sub-123", "user@example.com", "Test User");

        var user = await svc.ProvisionAsync(principal, "azure");

        user.ExternalId.Should().Be("sub-123");
        user.Email.Should().Be("user@example.com");
        user.AuthProvider.Should().Be("oidc:azure");
        _db.Users.Count(u => u.ExternalId == "sub-123").Should().Be(1);
    }

    [Fact]
    public async Task ProvisionAsync_ReturnsExistingUser_WhenAlreadyProvisioned()
    {
        _db.Users.Add(new SyncUser
        {
            ExternalId   = "sub-456",
            AuthProvider = "oidc:google",
            Email        = "existing@example.com",
            Username     = "existing@example.com",
            PasswordHash = string.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new OidcUserProvisioningService(_db);
        var principal = MakePrincipal("sub-456", "existing@example.com", "Existing");

        var user = await svc.ProvisionAsync(principal, "google");

        user.ExternalId.Should().Be("sub-456");
        _db.Users.Count().Should().Be(1);
    }

    [Fact]
    public async Task ProvisionAsync_Throws_WhenSubClaimMissing()
    {
        var svc = new OidcUserProvisioningService(_db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "oidc"));

        var act = () => svc.ProvisionAsync(principal, "test");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sub*");
    }
}
