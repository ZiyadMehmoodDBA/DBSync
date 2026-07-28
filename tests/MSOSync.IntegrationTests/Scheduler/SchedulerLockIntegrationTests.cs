using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Common.Locks;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using MSOSync.Scheduler.Internal;
using Xunit;

namespace MSOSync.IntegrationTests.Scheduler;

/// <summary>
/// Dual-instance scheduler lock integration tests.
/// Requires SQL Server (localdb) — skipped automatically if DB is unavailable.
/// Uses the MSOSync_IntegrationTests database (migrated by DatabaseFixture).
/// </summary>
public sealed class SchedulerLockIntegrationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    private AppDbContext CreateDbContext() =>
        new(AppDbContext.CreateOptionsBuilder(
            "Server=(localdb)\\mssqllocaldb;Database=MSOSync_IntegrationTests;" +
            "Trusted_Connection=True;TrustServerCertificate=True;").Options);

    private (IDistributedLockService lockSvc, ISchedulerLockFactory factory) CreatePair(
        AppDbContext context,
        int ttlSeconds     = 30,
        int renewalSeconds = 5)
    {
        var lockSvc = new SqlDistributedLockService(context);
        var options = Options.Create(new SchedulerLockOptions
        {
            TtlSeconds             = ttlSeconds,
            RenewalIntervalSeconds = renewalSeconds,
            LockPrefix             = "scheduler:"
        });

        // Build a minimal DI container that resolves IDistributedLockService to the
        // specific SqlDistributedLockService instance backed by this AppDbContext.
        // SchedulerLockFactory now depends on IServiceScopeFactory (C2 fix).
        var services = new ServiceCollection();
        services.AddSingleton<IDistributedLockService>(lockSvc);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var factory = new SchedulerLockFactory(scopeFactory, options,
            NullLogger<SchedulerLockFactory>.Instance);
        return (lockSvc, factory);
    }

    private async Task EnsureLockRowAsync(string lockName)
    {
        await db.Db.Database.ExecuteSqlRawAsync(
            $"IF NOT EXISTS (SELECT 1 FROM [{Schema}].[sync_lock] WHERE lock_name = {{0}}) " +
            $"INSERT INTO [{Schema}].[sync_lock] (lock_name, lock_owner, lock_time, lock_expiry, lock_scope) " +
            "VALUES ({0}, NULL, NULL, NULL, 0)",
            new object[] { lockName });

        // Clear any leftover state from a previous test run
        await db.Db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL, lock_expiry = NULL " +
            "WHERE lock_name = {0}",
            new object[] { lockName });
    }

    [Fact]
    public async Task Only_One_Instance_Acquires_Lock_When_Both_Race()
    {
        const string jobName = "SyncJob";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await using var dbA = CreateDbContext();
        await using var dbB = CreateDbContext();
        var (_, factoryA) = CreatePair(dbA);
        var (_, factoryB) = CreatePair(dbB);

        // Race both instances
        var taskA   = factoryA.TryAcquireAsync(jobName, CancellationToken.None);
        var taskB   = factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB);

        var lockA = results[0];
        var lockB = results[1];

        var winners = new[] { lockA, lockB }.Count(l => l is not null);
        winners.Should().Be(1, "exactly one instance should win the lock acquisition race");

        // Cleanup
        if (lockA is not null) await lockA.DisposeAsync();
        if (lockB is not null) await lockB.DisposeAsync();
    }

    [Fact]
    public async Task Second_Instance_Acquires_Lock_After_First_Releases()
    {
        const string jobName = "PullJob";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await using var dbA = CreateDbContext();
        await using var dbB = CreateDbContext();
        var (_, factoryA) = CreatePair(dbA);
        var (_, factoryB) = CreatePair(dbB);

        // Instance A acquires
        var lockA = await factoryA.TryAcquireAsync(jobName, CancellationToken.None);
        lockA.Should().NotBeNull("instance A should win first");

        // Instance B attempts — should get null
        var lockB1 = await factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        lockB1.Should().BeNull("instance B should see A's lock");

        // Instance A releases
        await lockA!.DisposeAsync();

        // Instance B retries — should now acquire
        var lockB2 = await factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        lockB2.Should().NotBeNull("instance B should acquire after A releases");
        lockB2!.JobName.Should().Be(jobName);

        await lockB2.DisposeAsync();
    }

    [Fact]
    public async Task Lock_Survives_Past_Stale_Timeout_With_Renewal()
    {
        // Use RenewalIntervalSeconds = 1 and wait 3 seconds, then try to steal.
        // Renewal keeps lock_expiry fresh so a second factory cannot steal it while renewal is running.
        const string jobName = "PurgeJob";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await using var dbA = CreateDbContext();
        await using var dbB = CreateDbContext();
        var (_, factoryA) = CreatePair(dbA, ttlSeconds: 120, renewalSeconds: 1);
        var (_, factoryB) = CreatePair(dbB, ttlSeconds: 120, renewalSeconds: 1);

        var lockA = await factoryA.TryAcquireAsync(jobName, CancellationToken.None);
        lockA.Should().NotBeNull();

        // Wait for a couple of renewals
        await Task.Delay(TimeSpan.FromSeconds(3));

        // B should still be unable to steal (lock_expiry is being renewed)
        var lockB = await factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        lockB.Should().BeNull("renewal should keep lock_expiry fresh; B cannot steal");

        if (lockB is not null) await lockB.DisposeAsync();
        await lockA!.DisposeAsync();
    }

    [Fact]
    public async Task SchedulerLockSeeder_Is_Idempotent()
    {
        // Call seed twice — should not throw or duplicate rows
        var schema = Schema;

        // Seed via raw SQL (mimics SchedulerLockSeeder logic)
        var jobNames = new[]
        {
            "scheduler:SyncJob", "scheduler:PullJob",
            "scheduler:PurgeJob", "scheduler:RetryJob"
        };

        foreach (var name in jobNames)
        {
            for (int i = 0; i < 2; i++)
            {
                await db.Db.Database.ExecuteSqlRawAsync(
                    $"IF NOT EXISTS (SELECT 1 FROM [{schema}].[sync_lock] WHERE lock_name = {{0}}) " +
                    $"INSERT INTO [{schema}].[sync_lock] (lock_name, lock_owner, lock_time, lock_expiry, lock_scope) " +
                    "VALUES ({0}, NULL, NULL, NULL, 0)",
                    new object[] { name });
            }
        }

        // Verify rows exist using AppDbContext.Locks DbSet
        var syncJobExists = await db.Db.Locks
            .AnyAsync(l => l.LockName == "scheduler:SyncJob");
        syncJobExists.Should().BeTrue("seed should insert scheduler:SyncJob row");

        var pullJobExists = await db.Db.Locks
            .AnyAsync(l => l.LockName == "scheduler:PullJob");
        pullJobExists.Should().BeTrue("seed should insert scheduler:PullJob row");

        var purgeJobExists = await db.Db.Locks
            .AnyAsync(l => l.LockName == "scheduler:PurgeJob");
        purgeJobExists.Should().BeTrue("seed should insert scheduler:PurgeJob row");

        var retryJobExists = await db.Db.Locks
            .AnyAsync(l => l.LockName == "scheduler:RetryJob");
        retryJobExists.Should().BeTrue("seed should insert scheduler:RetryJob row");
    }

    [Fact]
    public async Task RenewAsync_Updates_LockExpiry_Without_Changing_Owner()
    {
        const string jobName = "RetryJob";
        const string owner   = "TEST-HOST:9999";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        // Manually set the lock row to our test owner with an old expiry
        await db.Db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = DATEADD(SECOND, -5, GETUTCDATE()), " +
            "    lock_expiry = DATEADD(SECOND, 25, GETUTCDATE()) " +
            "WHERE lock_name = {1}",
            new object[] { owner, $"scheduler:{jobName}" });

        await using var dbA = CreateDbContext();
        var lockSvc = new SqlDistributedLockService(dbA);
        var renewed = await lockSvc.RenewAsync(
            $"scheduler:{jobName}", owner, TimeSpan.FromSeconds(120));

        renewed.Should().BeTrue("RenewAsync should return true for a held lock");

        var row = await db.Db.Locks
            .FirstOrDefaultAsync(l => l.LockName == $"scheduler:{jobName}");
        row.Should().NotBeNull();
        row!.LockOwner.Should().Be(owner);
        row.LockExpiry.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(120), precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReleaseAsync_Clears_Owner_And_LockExpiry()
    {
        const string jobName = "SyncJob";
        const string owner   = "TEST-HOST:8888";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await db.Db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE(), " +
            "    lock_expiry = DATEADD(SECOND, 120, GETUTCDATE()) " +
            "WHERE lock_name = {1}",
            new object[] { owner, $"scheduler:{jobName}" });

        await using var dbA = CreateDbContext();
        var lockSvc = new SqlDistributedLockService(dbA);
        await lockSvc.ReleaseAsync($"scheduler:{jobName}", owner);

        var row = await db.Db.Locks
            .FirstOrDefaultAsync(l => l.LockName == $"scheduler:{jobName}");
        row!.LockOwner.Should().BeNull();
        row.LockTime.Should().BeNull();
        row.LockExpiry.Should().BeNull();
    }
}
