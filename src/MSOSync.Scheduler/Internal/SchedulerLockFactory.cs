using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Locks;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Acquires per-job scheduler locks against the <see cref="IDistributedLockService"/>.
/// The lock name used is "{LockPrefix}{jobName}" (e.g., "scheduler:SyncJob").
///
/// Uses IServiceScopeFactory (not a direct IDistributedLockService dependency) to avoid
/// the captive-dependency problem: IDistributedLockService is Scoped (it wraps a scoped
/// DbContext), while this factory is Singleton. Each TryAcquireAsync call creates its own
/// DI scope and resolves IDistributedLockService from that scope. The scope is passed to
/// SchedulerLockImpl, which owns its lifetime and disposes it on DisposeAsync.
/// </summary>
internal sealed class SchedulerLockFactory(
    IServiceScopeFactory           scopeFactory,
    IOptions<SchedulerLockOptions> options,
    ILogger<SchedulerLockFactory>  logger) : ISchedulerLockFactory
{
    private readonly SchedulerLockOptions _options = options.Value;

    public async Task<ISchedulerLock?> TryAcquireAsync(string jobName, CancellationToken ct)
    {
        var lockName = $"{_options.LockPrefix}{jobName}";
        var expiry   = TimeSpan.FromSeconds(_options.TtlSeconds);
        var owner    = $"{Environment.MachineName}:{Environment.ProcessId}";

        // Create a dedicated scope per acquire attempt. The scope is owned by SchedulerLockImpl
        // and disposed when the lock is released.
        var scope       = scopeFactory.CreateScope();
        var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();

        IDistributedLock? handle;
        try
        {
            handle = await lockService.TryAcquireAsync(lockName, owner, expiry, ct);
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        if (handle is null)
        {
            scope.Dispose();
            logger.LogDebug(
                "SchedulerLockFactory: lock '{LockName}' is held — skipping",
                lockName);
            return null;
        }

        // The raw IDistributedLock handle is intentionally left undisposed here.
        // SqlDistributedLock has no finalizer; SchedulerLockImpl manages lock lifecycle
        // (renewal + release) using the lockService from the same scope.
        _ = handle;

        logger.LogDebug(
            "SchedulerLockFactory: acquired '{LockName}' (owner={Owner})",
            lockName, owner);

        // Pass the scope to SchedulerLockImpl. It will dispose the scope on DisposeAsync,
        // which also disposes the DbContext / lockService.
        return SchedulerLockImpl.Create(jobName, lockService, scope, _options, logger);
    }
}
