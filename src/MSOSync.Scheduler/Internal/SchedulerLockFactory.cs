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

        // Do NOT dispose the raw handle here — doing so would immediately release the lock row
        // (SqlDistributedLock.DisposeAsync sets lock_owner = NULL). SchedulerLockImpl takes
        // over the lock lifecycle: it renews lock_expiry via IDistributedLockService and
        // releases via ReleaseAsync on its own DisposeAsync. The raw handle is intentionally
        // left undisposed; SqlDistributedLock has no finalizer, so the GC will reclaim it
        // without triggering any database operation.
        _ = handle; // acknowledge the discard; handle is owned by SchedulerLockImpl going forward

        logger.LogDebug(
            "SchedulerLockFactory: acquired '{LockName}' (owner={Owner})",
            lockName, owner);

        return new SchedulerLockImpl(jobName, lockService, _options, logger);
    }
}
