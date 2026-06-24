using FluentAssertions;
using MSOSync.Metadata.Users;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class UsersManagementServiceTests
{
    private static (UsersManagementService Svc, MSOSync.Persistence.AppDbContext Db) Make()
    {
        var db     = TestDbContext.Create();
        var hasher = new BCryptPasswordHasher();
        var svc    = new UsersManagementService(db, hasher);
        return (svc, db);
    }

    [Fact]
    public async Task CreateUserAsync_HashesPassword_ReturnsDto()
    {
        var (svc, db) = Make();

        var result = await svc.CreateUserAsync(
            new CreateUserRequest("alice", "P@ss1234!", true));

        result.Username.Should().Be("alice");
        result.Enabled.Should().BeTrue();

        var stored = await db.Users.FindAsync(result.UserId);
        stored!.PasswordHash.Should().NotBe("P@ss1234!");
        stored.PasswordHash.Should().StartWith("$2");
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateUsername_ThrowsInvalidOperation()
    {
        var (svc, _) = Make();
        await svc.CreateUserAsync(new CreateUserRequest("bob", "P@ss1234!", true));

        var act = async () => await svc.CreateUserAsync(new CreateUserRequest("bob", "P@ss1234!", true));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bob*");
    }

    [Fact]
    public async Task UpdateUserAsync_ResetPassword_RevokesTokens()
    {
        var (svc, db) = Make();
        var hasher    = new BCryptPasswordHasher();

        var created = await svc.CreateUserAsync(new CreateUserRequest("carol", "P@ss1234!", true));

        // Seed a live refresh token
        db.UserRefreshTokens.Add(new SyncUserRefreshToken
        {
            UserId          = created.UserId,
            TokenHash       = hasher.Hash("rawtoken"),
            TokenLookupHash = "aaaa" + new string('0', 60),
            IssuedAt        = DateTime.UtcNow,
            ExpiresAt       = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync();

        await svc.UpdateUserAsync(created.UserId, new UpdateUserRequest(null, "NewP@ss1!"));

        var token = await db.UserRefreshTokens.FindAsync(1L);
        token!.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeactivateUserAsync_SetsEnabledFalse_RevokesTokens()
    {
        var (svc, db) = Make();
        var hasher    = new BCryptPasswordHasher();

        var created = await svc.CreateUserAsync(new CreateUserRequest("dave", "P@ss1234!", true));

        db.UserRefreshTokens.Add(new SyncUserRefreshToken
        {
            UserId          = created.UserId,
            TokenHash       = hasher.Hash("rawtoken2"),
            TokenLookupHash = "bbbb" + new string('0', 60),
            IssuedAt        = DateTime.UtcNow,
            ExpiresAt       = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync();

        await svc.DeactivateUserAsync(created.UserId);

        db.ChangeTracker.Clear();
        var user  = await db.Users.FindAsync(created.UserId);
        var token = await db.UserRefreshTokens.FindAsync(1L);

        user!.Enabled.Should().BeFalse();
        token!.RevokedAt.Should().NotBeNull();
    }
}
