using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Locks;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Acquires per-job scheduler locks against the <see cref="IDistributedLockService"/>.
/// The lock name used is "{LockPrefix}{jobName}" (e.g., "scheduler:SyncJob").
/// </summary>
internal sealed class SchedulerLockFactory(
    IDistributedLockService        lockService,
    IOptions<SchedulerLockOptions> options,
    ILogger<SchedulerLockFactory>  logger) : ISchedulerLockFactory
{
    private readonly SchedulerLockOptions _options = options.Value;

    public async Task<ISchedulerLock?> TryAcquireAsync(string jobName, CancellationToken ct)
    {
        var lockName = $"{_options.LockPrefix}{jobName}";
        var expiry   = TimeSpan.FromSeconds(_options.TtlSeconds);
        var owner    = $"{Environment.MachineName}:{Environment.ProcessId}";

        var handle = await lockService.TryAcquireAsync(lockName, owner, expiry, ct);

        if (handle is null)
        {
            logger.LogDebug(
                "SchedulerLockFactory: lock '{LockName}' is held — skipping",
                lockName);
            return null;
        }

        // Dispose the raw IDistributedLock handle — SchedulerLockImpl takes over lifecycle
        // (renewal and release) via IDistributedLockService directly.
        await handle.DisposeAsync();

        logger.LogDebug(
            "SchedulerLockFactory: acquired '{LockName}' (owner={Owner})",
            lockName, owner);

        return new SchedulerLockImpl(jobName, lockService, _options, logger);
    }
}
