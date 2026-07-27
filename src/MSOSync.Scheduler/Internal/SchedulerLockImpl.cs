using Microsoft.Extensions.Logging;
using MSOSync.Common.Locks;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Concrete scheduler lock. Starts a heartbeat renewal Task immediately
/// after construction so the lock never goes stale while work is running.
/// </summary>
internal sealed class SchedulerLockImpl : ISchedulerLock
{
    private readonly IDistributedLockService  _lockService;
    private readonly SchedulerLockOptions     _options;
    private readonly ILogger                  _logger;
    private readonly CancellationTokenSource  _renewalCts = new();
    private readonly Task                     _renewalTask;

    public string         JobName    { get; }
    public DateTimeOffset AcquiredAt { get; } = DateTimeOffset.UtcNow;
    public string         Owner      { get; } =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    internal SchedulerLockImpl(
        string                  jobName,
        IDistributedLockService lockService,
        SchedulerLockOptions    options,
        ILogger                 logger)
    {
        JobName      = jobName;
        _lockService = lockService;
        _options     = options;
        _logger      = logger;
        _renewalTask = RunRenewalLoopAsync(_renewalCts.Token);
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
    }
}
