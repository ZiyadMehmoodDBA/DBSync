using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Persistence;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Inserts the four "scheduler:*" rows into sync_lock at startup if they do not exist.
/// No schema change — data seed only.
/// </summary>
internal sealed class SchedulerLockSeeder(
    IServiceScopeFactory         scopeFactory,
    ILogger<SchedulerLockSeeder> logger) : IHostedService
{
    private static readonly string[] JobNames =
    [
        "scheduler:SyncJob",
        "scheduler:PullJob",
        "scheduler:PurgeJob",
        "scheduler:RetryJob"
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

        try
        {
            foreach (var lockName in JobNames)
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"IF NOT EXISTS (SELECT 1 FROM [{schema}].[sync_lock] WHERE lock_name = {{0}}) " +
                    $"INSERT INTO [{schema}].[sync_lock] (lock_name, lock_owner, lock_time, lock_expiry, lock_scope) " +
                    "VALUES ({0}, NULL, NULL, NULL, 0)",
                    new object[] { lockName },
                    cancellationToken);
            }

            logger.LogInformation(
                "SchedulerLockSeeder: scheduler lock rows seeded ({Count} jobs)",
                JobNames.Length);
        }
        catch (Exception ex)
        {
            // I7: seeding failure is fatal — without lock rows, all jobs silently return Standby
            // and no synchronisation work is performed. Fail fast so the problem is surfaced
            // immediately rather than silently degrading the system.
            logger.LogCritical(ex,
                "SchedulerLockSeeder: failed to seed scheduler lock rows — application cannot start safely");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
