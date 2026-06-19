using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.SecurityTests;

public sealed class UserServiceTests
{
    private static AppDbContext MakeDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public async Task FindByUsername_ExistingUser_ReturnsUser()
    {
        await using var db = MakeDbContext();
        db.Users.Add(new SyncUser { UserId = 1, Username = "alice", PasswordHash = "hash", Enabled = true });
        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var user = await svc.FindByUsernameAsync("alice");

        user.Should().NotBeNull();
        user!.Username.Should().Be("alice");
    }

    [Fact]
    public async Task FindByUsername_MissingUser_ReturnsNull()
    {
        await using var db = MakeDbContext();
        var svc = new UserService(db);
        var user = await svc.FindByUsernameAsync("nobody");
        user.Should().BeNull();
    }

    [Fact]
    public async Task GetRoles_UserWithRoles_ReturnsList()
    {
        await using var db = MakeDbContext();
        db.Users.Add(new SyncUser { UserId = 1, Username = "bob", PasswordHash = "hash", Enabled = true });
        db.Roles.Add(new SyncRole { RoleId = 10, RoleName = "ADMIN" });
        db.UserRoles.Add(new SyncUserRole { UserId = 1, RoleId = 10 });
        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var roles = await svc.GetRolesAsync(1L);

        roles.Should().ContainSingle().Which.Should().Be("ADMIN");
    }
}
