using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Auth;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Auth;

public sealed class ApiKeyServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ApiKeyServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private ApiKeyService Build() => new(_db);

    // ── User API keys ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserKeyAsync_ReturnsRawKey_WithMskPrefix()
    {
        _db.Users.Add(new SyncUser { UserId = 1, Username = "test", PasswordHash = "x" });
        await _db.SaveChangesAsync();

        var (rawKey, entity) = await Build().CreateUserKeyAsync(1, "Test Key");

        rawKey.Should().StartWith("msk_");
        // Format: msk_{8}_{32} = 4+8+1+32 = 45 chars; secret payload may vary in length
        rawKey.Length.Should().BeGreaterThan(10);
        // KeyPrefix stored in DB is the first 8 chars of rawKey
        entity.KeyPrefix.Should().Be(rawKey[..8]);
        entity.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateUserKeyAsync_ReturnsUser_ForValidKey()
    {
        _db.Users.Add(new SyncUser { UserId = 2, Username = "alice", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var (rawKey, _) = await Build().CreateUserKeyAsync(2, "Key");

        var user = await Build().ValidateUserKeyAsync(rawKey);

        user.Should().NotBeNull();
        user!.UserId.Should().Be(2);
    }

    [Fact]
    public async Task ValidateUserKeyAsync_ReturnsNull_ForInvalidKey()
    {
        var user = await Build().ValidateUserKeyAsync("msk_inv");
        user.Should().BeNull();
    }

    [Fact]
    public async Task ValidateUserKeyAsync_ReturnsNull_ForWrongHash()
    {
        // A valid-length key that won't match any stored hash
        var user = await Build().ValidateUserKeyAsync("msk_aaaabbbb_ccccddddeeeeffffgggghhhhiiiijjjj");
        user.Should().BeNull();
    }

    [Fact]
    public async Task ValidateUserKeyAsync_ReturnsNull_WhenRevoked()
    {
        _db.Users.Add(new SyncUser { UserId = 3, Username = "bob", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var (rawKey, entity) = await Build().CreateUserKeyAsync(3, "Key");
        await Build().RevokeUserKeyAsync(entity.Id);

        var user = await Build().ValidateUserKeyAsync(rawKey);

        user.Should().BeNull();
    }

    [Fact]
    public async Task RevokeUserKeyAsync_SetsIsRevokedTrue()
    {
        _db.Users.Add(new SyncUser { UserId = 4, Username = "carol", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var (_, entity) = await Build().CreateUserKeyAsync(4, "Key");

        await Build().RevokeUserKeyAsync(entity.Id);

        var key = await _db.UserApiKeys.FindAsync(entity.Id);
        key!.IsRevoked.Should().BeTrue();
        key.RevokedAt.Should().NotBeNull();
    }

    // ── Service accounts ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateServiceAccountAsync_ReturnsRawKey_WithMsaPrefix()
    {
        var (rawKey, entity) = await Build().CreateServiceAccountAsync("CI Bot", ["read", "write"]);

        rawKey.Should().StartWith("msa_");
        entity.Name.Should().Be("CI Bot");
        entity.ClientId.Should().NotBeNullOrEmpty();
        // Permissions stored as JSON in Description
        var perms = JsonSerializer.Deserialize<string[]>(entity.Description!);
        perms.Should().Contain("read");
        perms.Should().Contain("write");
    }

    [Fact]
    public async Task ValidateServiceAccountKeyAsync_ReturnsAccount_ForValidKey()
    {
        var (rawKey, _) = await Build().CreateServiceAccountAsync("Bot", ["read"]);

        var account = await Build().ValidateServiceAccountKeyAsync(rawKey);

        account.Should().NotBeNull();
        account!.Name.Should().Be("Bot");
    }

    [Fact]
    public async Task ValidateServiceAccountKeyAsync_ReturnsNull_ForInvalidKey()
    {
        var account = await Build().ValidateServiceAccountKeyAsync("msa_invalidkey");
        account.Should().BeNull();
    }

    [Fact]
    public async Task ValidateServiceAccountKeyAsync_ReturnsNull_WhenRevoked()
    {
        var (rawKey, entity) = await Build().CreateServiceAccountAsync("Bot", ["read"]);
        await Build().RevokeServiceAccountAsync(entity.Id);

        var account = await Build().ValidateServiceAccountKeyAsync(rawKey);

        account.Should().BeNull();
    }

    [Fact]
    public async Task RevokeServiceAccountAsync_SetsIsRevokedTrue()
    {
        var (_, entity) = await Build().CreateServiceAccountAsync("Bot", []);

        await Build().RevokeServiceAccountAsync(entity.Id);

        var acct = await _db.ServiceAccounts.FindAsync(entity.Id);
        acct!.IsRevoked.Should().BeTrue();
        acct.RevokedAt.Should().NotBeNull();
    }
}
