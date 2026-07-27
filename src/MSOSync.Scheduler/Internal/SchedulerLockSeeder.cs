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
            // Non-fatal at startup — rows may already exist or DB may not be ready yet.
            // Jobs will fail to acquire locks on first tick if rows are missing.
            logger.LogWarning(ex,
                "SchedulerLockSeeder: failed to seed scheduler lock rows — jobs may not distribute correctly");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
