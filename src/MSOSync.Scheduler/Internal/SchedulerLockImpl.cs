using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Locks;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Concrete scheduler lock. Starts a heartbeat renewal Task after the object is fully
/// constructed (via the static <see cref="Create"/> factory method) so that the renewal
/// loop never fires against a partially-initialised object.
///
/// The DI <see cref="IServiceScope"/> that owns the <see cref="IDistributedLockService"/>
/// is disposed on <see cref="DisposeAsync"/>, ensuring the underlying DbContext is not
/// shared across concurrent operations.
/// </summary>
internal sealed class SchedulerLockImpl : ISchedulerLock
{
    private readonly IDistributedLockService  _lockService;
    private readonly SchedulerLockOptions     _options;
    private readonly ILogger                  _logger;
    private readonly IServiceScope            _scope;
    private readonly CancellationTokenSource  _renewalCts = new();
    private readonly Task                     _renewalTask;

    public string         JobName    { get; }
    public DateTimeOffset AcquiredAt { get; } = DateTimeOffset.UtcNow;
    public string         Owner      { get; } =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    private SchedulerLockImpl(
        string                  jobName,
        IDistributedLockService lockService,
        IServiceScope           scope,
        SchedulerLockOptions    options,
        ILogger                 logger,
        Task                    renewalTask)
    {
        JobName      = jobName;
        _lockService = lockService;
        _scope       = scope;
        _options     = options;
        _logger      = logger;
        _renewalTask = renewalTask;
    }

    /// <summary>
    /// Factory method — fully constructs the object then starts the renewal loop.
    /// Starting the Task here (outside the constructor) avoids leaking <c>this</c>
    /// before construction is complete.
    /// </summary>
    internal static SchedulerLockImpl Create(
        string                  jobName,
        IDistributedLockService lockService,
        IServiceScope           scope,
        SchedulerLockOptions    options,
        ILogger                 logger)
    {
        // Temporary CTS so we can wire up the task. We capture the real CTS inside
        // the instance after construction.
        var cts = new CancellationTokenSource();

        // Build the instance without starting the task yet.
        // We need a forward-reference trick: allocate renewal task slot then fill it.
        SchedulerLockImpl? instance = null;

        Task renewalTask = Task.Run(async () =>
        {
            // Wait until the instance reference is set (happens synchronously before
            // this lambda can run, since Task.Run posts to the thread pool).
            while (instance is null) await Task.Yield();
            await instance.RunRenewalLoopAsync(instance._renewalCts.Token);
        });

        instance = new SchedulerLockImpl(jobName, lockService, scope, options, logger, renewalTask);
        return instance;
    }

    private async Task RunRenewalLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.RenewalIntervalSeconds);
        var expiry   = TimeSpan.FromSeconds(_options.TtlSeconds);
        var lockName = $"{_options.LockPrefix}{JobName}";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                await _lockService.RenewAsync(lockName, Owner, expiry, ct);
                _logger.LogDebug(
                    "SchedulerLock: renewed {JobName} (owner={Owner})",
                    JobName, Owner);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Non-fatal: log warning; renewal retries next interval.
                // If renewals fail until TTL expires the lock will be stolen — safe fallback.
                _logger.LogWarning(ex,
                    "SchedulerLock: renewal failed for {JobName} — lock may expire if this persists",
                    JobName);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 1. Stop the renewal loop.
        await _renewalCts.CancelAsync();
        try { await _renewalTask.ConfigureAwait(false); }
        catch { /* swallow OperationCanceledException */ }
        _renewalCts.Dispose();

        // 2. Release the lock row immediately (do not pass a cancellation token —
        //    release must complete even during application shutdown).
        //    Swallow any exception so Dispose never throws (best-effort release).
        var lockName = $"{_options.LockPrefix}{JobName}";
        try
        {
            await _lockService.ReleaseAsync(lockName, Owner).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SchedulerLock: release failed for {JobName} — lock will expire naturally",
                JobName);
        }

        // 3. Dispose the DI scope (releases the DbContext and IDistributedLockService).
        _scope.Dispose();
    }
}
