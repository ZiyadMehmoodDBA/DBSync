using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.SecurityTests;

public sealed class NodeSecurityServiceTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Validate_CurrentToken_ReturnsTrue()
    {
        await using var db = MakeDb();
        var hasher = new BCryptPasswordHasher();
        const string raw = "my-node-token";
        db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = "node1",
            CurrentTokenHash = hasher.Hash(raw)
        });
        await db.SaveChangesAsync();

        var svc = new NodeSecurityService(db, hasher);
        (await svc.ValidateTokenAsync("node1", raw)).Should().BeTrue();
    }

    [Fact]
    public async Task Validate_NextToken_ReturnsTrueDuringRotation()
    {
        await using var db = MakeDb();
        var hasher = new BCryptPasswordHasher();
        const string currentRaw = "current-token";
        const string nextRaw = "next-token";
        db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = "node1",
            CurrentTokenHash = hasher.Hash(currentRaw),
            NextTokenHash = hasher.Hash(nextRaw)
        });
        await db.SaveChangesAsync();

        var svc = new NodeSecurityService(db, hasher);
        (await svc.ValidateTokenAsync("node1", nextRaw)).Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WrongToken_ReturnsFalse()
    {
        await using var db = MakeDb();
        var hasher = new BCryptPasswordHasher();
        db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = "node1",
            CurrentTokenHash = hasher.Hash("correct-token")
        });
        await db.SaveChangesAsync();

        var svc = new NodeSecurityService(db, hasher);
        (await svc.ValidateTokenAsync("node1", "wrong-token")).Should().BeFalse();
    }

    [Fact]
    public async Task Validate_UnknownNode_ReturnsFalse()
    {
        await using var db = MakeDb();
        var hasher = new BCryptPasswordHasher();
        var svc = new NodeSecurityService(db, hasher);
        (await svc.ValidateTokenAsync("ghost", "token")).Should().BeFalse();
    }
}
