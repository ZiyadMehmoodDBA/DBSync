# Background Worker Inventory

All recurring background workers in MSOSync follow the standard pattern:
register with `IWorkerStatusRegistry`, call `RecordTickStart` at the top
of each cycle, `RecordTickComplete` on success, and `RecordTickFailed`
on exception. Workers use `PeriodicTimer` for scheduling unless a
wall-clock schedule or polling loop is required (see exemptions below).

## Standard Pattern

```csharp
public override async Task StartAsync(CancellationToken cancellationToken)
{
    registry.Register(nameof(WorkerName), interval);
    await base.StartAsync(cancellationToken);
}

protected override async Task ExecuteAsync(CancellationToken ct)
{
    using var timer = new PeriodicTimer(interval);
    while (await timer.WaitForNextTickAsync(ct))
    {
        registry.RecordTickStart(nameof(WorkerName));
        try
        {
            await DoWorkAsync(ct);
            registry.RecordTickComplete(nameof(WorkerName));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(WorkerName), ex);
        }
    }
}
```

## Worker Inventory

| Worker | Project | Interval | Registry | PeriodicTimer | Notes |
|---|---|---|---|---|---|
| `SyncJob` | MSOSync.Scheduler | 30s (SyncOptions) | ✅ | ✅ | Lock-guarded |
| `PullJob` | MSOSync.Scheduler | 10s (SyncOptions) | ✅ | ✅ | Push-mode disable |
| `RetryJob` | MSOSync.Scheduler | 5min (fixed) | ✅ | ✅ | Fixed cadence acceptable |
| `PurgeJob` | MSOSync.Scheduler | 24h (daily 02:00 UTC) | ✅ | ❌ (Task.Delay) | Wall-clock schedule — PeriodicTimer exempt |
| `HeartbeatWorker` | MSOSync.Scheduler | 30s (HeartbeatOptions) | ✅ | ✅ | |
| `ProbeWorker` | MSOSync.Scheduler | 60s (HeartbeatOptions) | ✅ | ✅ | Hub-only |
| `ConnectivityEvaluator` | MSOSync.Scheduler | LifecycleOptions | ✅ | ✅ | Hub-only, skip-on-overlap |
| `DecommissionWorker` | MSOSync.Scheduler | LifecycleOptions | ✅ | ✅ | Hub-only |
| `ExportJobWorker` | MSOSync.App | 5s (fixed) | ✅ | ❌ (Task.Delay) | Job-polling loop — PeriodicTimer exempt |
| `ExportCleanupWorker` | MSOSync.App | 1h (fixed) | ✅ | ❌ (Task.Delay) | Cleanup loop — PeriodicTimer exempt |
| `RollingOperationWorker` | MSOSync.App | 15s (LifecycleOptions.RollingWorkerIntervalSeconds) | ✅ | ✅ | Advances wave-by-wave; drain → maintain → verify |
| `ReplayWorker` | MSOSync.Scheduler | 10s (ReplayOptions.WorkerIntervalSeconds) | ✅ | ✅ | Advances BatchReplay operations; FailedDelivery resets to Retry, MissedData calls IBatchCreator |
| `AdminBootstrapper` | MSOSync.App | One-shot | N/A | N/A | One-shot startup task — registry exempt |

## PeriodicTimer Exemptions

- **PurgeJob**: Fires at exactly 02:00 UTC daily. `PeriodicTimer` measures elapsed time from start, not wall-clock time. `Task.Delay(TimeUntilNextFire())` is the correct pattern.
- **ExportJobWorker**: Polls for pending export jobs every 5 seconds in a tight loop. The delay is between iterations, not a fixed schedule.
- **ExportCleanupWorker**: Runs expiry logic hourly in a loop. Same rationale as ExportJobWorker.

## IWorkerStatusRegistry Compliance Status

- ✅ **12/12 active workers** comply with registry pattern (SyncJob, PullJob, RetryJob, PurgeJob, HeartbeatWorker, ProbeWorker, ConnectivityEvaluator, DecommissionWorker, ExportJobWorker, ExportCleanupWorker, RollingOperationWorker, ReplayWorker)
- N/A **1 one-shot worker** exempt: AdminBootstrapper (startup-only, no loop)

## AdminBootstrapper

`AdminBootstrapper` runs once at startup to seed the default admin user. It is not a recurring worker and does not register with `IWorkerStatusRegistry`.
