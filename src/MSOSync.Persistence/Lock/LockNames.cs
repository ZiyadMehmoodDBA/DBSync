namespace MSOSync.Persistence.Lock;

public static class LockNames
{
    /// <summary>Legacy lock name for SyncJob. Superseded by "scheduler:SyncJob" in Phase 2D.3.</summary>
    [Obsolete("Use SchedulerJobGuard with ISchedulerLockFactory. Legacy lock name kept for burn-in period. Remove in 2D.3.1.")]
    public const string SyncEngine  = "SYNC_ENGINE";

    /// <summary>Legacy lock name for RetryJob. Superseded by "scheduler:RetryJob" in Phase 2D.3.</summary>
    [Obsolete("Use SchedulerJobGuard with ISchedulerLockFactory. Legacy lock name kept for burn-in period. Remove in 2D.3.1.")]
    public const string RetryEngine = "RETRY_ENGINE";

    /// <summary>Legacy lock name for PurgeJob. Superseded by "scheduler:PurgeJob" in Phase 2D.3.</summary>
    [Obsolete("Use SchedulerJobGuard with ISchedulerLockFactory. Legacy lock name kept for burn-in period. Remove in 2D.3.1.")]
    public const string PurgeEngine = "PURGE_ENGINE";
}
