using FluentAssertions;
using Microsoft.Extensions.Options;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class BootstrapTokenServiceTests
{
    private static (BootstrapTokenService Svc, AppDbContext Db, BCryptPasswordHasher Hasher) Make()
    {
        var db = TestDbContext.Create();
        // Bootstrap tokens carry an FK to the node — seed the parent row.
        db.Nodes.Add(new SyncNode
        {
            NodeId = "n1", GroupId = "g1", SyncUrl = "http://n",
            NodeName = "n1", ExternalId = "ext-1",
            LifecycleState = NodeLifecycleState.PendingRegistration,
        });
        db.SaveChanges();
        var hasher = new BCryptPasswordHasher();
        var svc = new BootstrapTokenService(db, hasher, Options.Create(new LifecycleOptions()));
        return (svc, db, hasher);
    }

    [Fact]
    public async Task Issue_RevokesPriorLiveTokens()
    {
        var (svc, db, _) = Make();

        await svc.IssueAsync("n1", "admin");
        await db.SaveChangesAsync();
        await svc.IssueAsync("n1", "admin");
        await db.SaveChangesAsync();

        db.NodeBootstrapTokens.Count(t => t.NodeId == "n1").Should().Be(2);
        db.NodeBootstrapTokens.Count(t => t.NodeId == "n1" && t.RevokedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task ValidateAndConsume_ValidToken_ReturnsTrue_MarksConsumed()
    {
        var (svc, db, _) = Make();
        var raw = await svc.IssueAsync("n1", "admin");
        await db.SaveChangesAsync();

        var ok = await svc.ValidateAndConsumeAsync("n1", raw);
        await db.SaveChangesAsync();

        ok.Should().BeTrue();
        db.NodeBootstrapTokens.Single().ConsumedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAndConsume_ConsumedToken_ReturnsFalse()
    {
        var (svc, db, _) = Make();
        var raw = await svc.IssueAsync("n1", "admin");
        await db.SaveChangesAsync();
        (await svc.ValidateAndConsumeAsync("n1", raw)).Should().BeTrue();
        await db.SaveChangesAsync();

        var replay = await svc.ValidateAndConsumeAsync("n1", raw);

        replay.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAndConsume_ExpiredToken_ReturnsFalse()
    {
        var (svc, db, _) = Make();
        var raw = await svc.IssueAsync("n1", "admin");
        db.NodeBootstrapTokens.Local.Single().ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        (await svc.ValidateAndConsumeAsync("n1", raw)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAndConsume_RevokedToken_ReturnsFalse()
    {
        var (svc, db, _) = Make();
        var raw = await svc.IssueAsync("n1", "admin");
        await db.SaveChangesAsync();
        await svc.RevokeAllAsync("n1");
        await db.SaveChangesAsync();

        (await svc.ValidateAndConsumeAsync("n1", raw)).Should().BeFalse();
    }

    [Fact]
    public async Task RawToken_NeverPersisted()
    {
        var (svc, db, hasher) = Make();
        var raw = await svc.IssueAsync("n1", "admin");
        await db.SaveChangesAsync();

        var stored = db.NodeBootstrapTokens.Single();
        stored.TokenHash.Should().NotBe(raw);                 // only the BCrypt hash at rest
        hasher.Verify(raw, stored.TokenHash).Should().BeTrue();
    }
}
