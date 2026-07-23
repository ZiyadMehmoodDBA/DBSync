using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Lock;

[Collection("LockIntegration")]
public sealed class SqlDistributedLockIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private string _connStr = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connStr = _container.GetConnectionString();

        // Apply migrations once
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connStr)
            .Options;
        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();

        // Seed lock row
        await db.Database.ExecuteSqlRawAsync(
            "IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'CONCURRENT_LOCK') " +
            "INSERT INTO [msosync].[sync_lock] (lock_name, lock_scope) VALUES ('CONCURRENT_LOCK', 0)");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connStr).Options);

    [Fact]
    public async Task TwoCallers_OnlyOneAcquires()
    {
        await using var db1 = NewDb();
        await using var db2 = NewDb();
        var svc1 = new SqlDistributedLockService(db1);
        var svc2 = new SqlDistributedLockService(db2);

        // Reset
        await db1.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] SET lock_owner=NULL,lock_time=NULL,lock_expiry=NULL " +
            "WHERE lock_name='CONCURRENT_LOCK'");

        // Both attempt concurrently
        var t1 = svc1.TryAcquireAsync("CONCURRENT_LOCK", "CALLER1", TimeSpan.FromSeconds(30));
        var t2 = svc2.TryAcquireAsync("CONCURRENT_LOCK", "CALLER2", TimeSpan.FromSeconds(30));
        var results = await Task.WhenAll(t1, t2);

        var acquired = results.Where(r => r is not null).ToList();
        acquired.Should().HaveCount(1);

        // Cleanup
        foreach (var h in results.Where(r => r is not null))
            await h!.DisposeAsync();
    }

    [Fact]
    public async Task ExpiredLock_StolenBySecondCaller()
    {
        await using var db1 = NewDb();
        await using var db2 = NewDb();
        var svc1 = new SqlDistributedLockService(db1);
        var svc2 = new SqlDistributedLockService(db2);

        // Reset
        await db1.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] SET lock_owner=NULL,lock_time=NULL,lock_expiry=NULL " +
            "WHERE lock_name='CONCURRENT_LOCK'");

        // First caller acquires with 100ms TTL
        var handle1 = await svc1.TryAcquireAsync("CONCURRENT_LOCK", "CALLER1", TimeSpan.FromMilliseconds(100));
        handle1.Should().NotBeNull();

        // Wait for expiry
        await Task.Delay(250);

        // Second caller steals the expired lock
        var handle2 = await svc2.TryAcquireAsync("CONCURRENT_LOCK", "CALLER2", TimeSpan.FromSeconds(30));
        handle2.Should().NotBeNull();
        handle2!.Owner.Should().Be("CALLER2");

        await handle2.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_ReleasesLock_AllowingReacquisition()
    {
        await using var db = NewDb();
        var svc = new SqlDistributedLockService(db);

        // Reset
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] SET lock_owner=NULL,lock_time=NULL,lock_expiry=NULL " +
            "WHERE lock_name='CONCURRENT_LOCK'");

        var handle = await svc.TryAcquireAsync("CONCURRENT_LOCK", "OWNER1", TimeSpan.FromSeconds(30));
        handle.Should().NotBeNull();

        await handle!.DisposeAsync();

        // Re-acquire immediately after dispose
        var handle2 = await svc.TryAcquireAsync("CONCURRENT_LOCK", "OWNER1", TimeSpan.FromSeconds(30));
        handle2.Should().NotBeNull();

        await handle2!.DisposeAsync();
    }
}

[CollectionDefinition("LockIntegration")]
public sealed class LockIntegrationCollectionDefinition { }
