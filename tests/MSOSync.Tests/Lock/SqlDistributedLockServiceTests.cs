using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.Tests.Lock;

[Collection("SqlLock")]
public sealed class SqlDistributedLockServiceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        _db = new AppDbContext(options);
        await _db.Database.MigrateAsync();

        // Seed a single lock row for each lock name under test
        await _db.Database.ExecuteSqlRawAsync(
            "IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'TEST_LOCK') " +
            "INSERT INTO [msosync].[sync_lock] (lock_name, lock_scope) VALUES ('TEST_LOCK', 0)");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private SqlDistributedLockService Svc() => new(_db);
    private static TimeSpan Expiry30s => TimeSpan.FromSeconds(30);

    // ── Reset helper ─────────────────────────────────────────────────────────
    private async Task ResetLockAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL, lock_expiry = NULL " +
            "WHERE lock_name = 'TEST_LOCK'");
    }

    // ── TryAcquireAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_ReturnsHandle_WhenLockFree()
    {
        await ResetLockAsync();

        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);

        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("TEST_LOCK");
        handle.Owner.Should().Be("OWNER1");
        handle.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(5));

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_ReturnsNull_WhenLockHeld()
    {
        await ResetLockAsync();
        var first = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        first.Should().NotBeNull();

        var second = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER2", Expiry30s);

        second.Should().BeNull();

        await first!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_Acquires_WhenLockExpired()
    {
        await ResetLockAsync();
        // Plant an expired lock directly
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = 'STALE', lock_time = DATEADD(SECOND, -60, GETUTCDATE()), " +
            "    lock_expiry = DATEADD(SECOND, -30, GETUTCDATE()) " +
            "WHERE lock_name = 'TEST_LOCK'");

        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER2", Expiry30s);

        handle.Should().NotBeNull();
        handle!.Owner.Should().Be("OWNER2");

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_Acquires_WhenLegacyStaleLock()
    {
        await ResetLockAsync();
        // Lock with no expiry column value but lock_time 11 minutes ago (legacy stale)
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = 'LEGACY', lock_time = DATEADD(MINUTE, -11, GETUTCDATE()), " +
            "    lock_expiry = NULL " +
            "WHERE lock_name = 'TEST_LOCK'");

        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER3", Expiry30s);

        handle.Should().NotBeNull();

        await handle!.DisposeAsync();
    }

    // ── RenewAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenewAsync_ReturnsTrue_WhenOwnerMatches()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        var renewed = await Svc().RenewAsync("TEST_LOCK", "OWNER1", TimeSpan.FromMinutes(5));

        renewed.Should().BeTrue();

        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task RenewAsync_ReturnsFalse_WhenOwnerMismatch()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        var renewed = await Svc().RenewAsync("TEST_LOCK", "WRONG_OWNER", TimeSpan.FromMinutes(5));

        renewed.Should().BeFalse();

        await handle!.DisposeAsync();
    }

    // ── ReleaseAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseAsync_ClearsRow_WhenOwnerMatches()
    {
        await ResetLockAsync();
        await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);

        await Svc().ReleaseAsync("TEST_LOCK", "OWNER1");

        var isHeld = await Svc().IsHeldAsync("TEST_LOCK");
        isHeld.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_NoOp_WhenOwnerMismatch()
    {
        await ResetLockAsync();
        await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);

        await Svc().ReleaseAsync("TEST_LOCK", "WRONG_OWNER");

        var isHeld = await Svc().IsHeldAsync("TEST_LOCK");
        isHeld.Should().BeTrue();   // still held by OWNER1

        await Svc().ReleaseAsync("TEST_LOCK", "OWNER1");
    }

    // ── IsHeldAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IsHeldAsync_ReturnsTrue_WhenActiveOwner()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        var held = await Svc().IsHeldAsync("TEST_LOCK");

        held.Should().BeTrue();
        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task IsHeldAsync_ReturnsFalse_WhenExpired()
    {
        await ResetLockAsync();
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = 'EXPIRED', lock_expiry = DATEADD(SECOND, -1, GETUTCDATE()) " +
            "WHERE lock_name = 'TEST_LOCK'");

        var held = await Svc().IsHeldAsync("TEST_LOCK");

        held.Should().BeFalse();
    }

    // ── DisposeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_ReleasesLock()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        await handle!.DisposeAsync();

        var held = await Svc().IsHeldAsync("TEST_LOCK");
        held.Should().BeFalse();
    }
}

[CollectionDefinition("SqlLock")]
public sealed class SqlLockCollectionDefinition { }
