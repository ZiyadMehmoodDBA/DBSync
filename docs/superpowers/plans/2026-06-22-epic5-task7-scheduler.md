# Task 7: MSOSync.Scheduler

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Scheduler/RecoveryReason.cs`
- Create: `src/MSOSync.Scheduler/SchedulerRecoveryEvent.cs`
- Create: `src/MSOSync.Scheduler/SchedulerRecovery.cs`
- Create: `src/MSOSync.Scheduler/SyncJob.cs`
- Create: `src/MSOSync.Scheduler/RetryJob.cs`
- Create: `src/MSOSync.Scheduler/PurgeJob.cs`
- Create: `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`
- Modify: `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj`
- Delete: `src/MSOSync.Scheduler/Placeholder.cs`

**Interfaces:**
- Consumes: `IDatabaseLockProvider`, `LockNames` (Task 1), `SyncEngine` (Task 6), `RetryProcessor`, `BatchPurger` (Task 5), `IEventPurger` (Task 3), `IClock` (Task 1), `AppDbContext` (for `SchedulerRecovery` batch queries), `IBatchStateMachine` (Task 5)
- Produces:
  - `RecoveryReason` enum: `Restart, OverdueRetry`
  - `SchedulerRecoveryEvent(int SentRecovered, int RetryRequeued) : INotification`
  - `SchedulerRecovery : IHostedService` — runs once on `StartAsync` before workers
  - `SyncJob : BackgroundService` — PeriodicTimer, acquires `LockNames.SyncEngine`
  - `RetryJob : BackgroundService` — every 5 min, acquires `LockNames.RetryEngine`
  - `PurgeJob : BackgroundService` — daily at 02:00 UTC
  - `AddSyncScheduler(IServiceCollection, IConfiguration)` extension — registers `SchedulerRecovery` first (order matters)

---

- [ ] **Step 1: Update `MSOSync.Scheduler.csproj`**

Current csproj references only Common and Engine. Add Persistence, Batch, Event:

```xml
<!-- src/MSOSync.Scheduler/MSOSync.Scheduler.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>SyncWorker, RetryWorker, HeartbeatWorker, PurgeWorker, MetricsWorker, NotificationWorker</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\MSOSync.Engine\MSOSync.Engine.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `RecoveryReason`**

```csharp
// src/MSOSync.Scheduler/RecoveryReason.cs
namespace MSOSync.Scheduler;

public enum RecoveryReason { Restart, OverdueRetry }
```

- [ ] **Step 3: Create `SchedulerRecoveryEvent`**

```csharp
// src/MSOSync.Scheduler/SchedulerRecoveryEvent.cs
using MediatR;

namespace MSOSync.Scheduler;

public sealed record SchedulerRecoveryEvent(
    int SentRecovered,
    int RetryRequeued) : INotification;
```

- [ ] **Step 4: Create `SchedulerRecovery`**

Runs once in `StartAsync`. Actions (in order):
1. `SENT → RETRY` for batches that were in-flight when the process restarted
2. Requeue overdue `RETRY` batches (`next_retry_time <= now`)
3. `NEW` — untouched; `SyncJob` picks up on first tick
4. Publish `SchedulerRecoveryEvent`

```csharp
// src/MSOSync.Scheduler/SchedulerRecovery.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Scheduler;

public sealed class SchedulerRecovery(
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerRecovery> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stateMachine = scope.ServiceProvider.GetRequiredService<IBatchStateMachine>();
        var clock        = scope.ServiceProvider.GetRequiredService<IClock>();
        var mediator     = scope.ServiceProvider.GetRequiredService<IMediator>();
        var now          = clock.UtcNow;

        // 1. SENT → RETRY (restart scenario — never ACKed)
        var sentBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Sent)
            .ToListAsync(ct);

        var sentRecovered = 0;
        foreach (var b in sentBatches)
        {
            if (await stateMachine.TransitionAsync(b.BatchId, BatchStatus.Sent, BatchStatus.Retry, ct))
            {
                sentRecovered++;
                logger.LogInformation("Recovery {Reason}: Batch {BatchId} SENT→RETRY",
                    RecoveryReason.Restart, b.BatchId);
            }
        }

        // 2. RETRY with overdue next_retry_time → requeue (reset to Retry so RetryJob picks up)
        var overdueBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Retry
                     && b.NextRetryTime != null
                     && b.NextRetryTime <= now)
            .ToListAsync(ct);

        var retryRequeued = 0;
        foreach (var b in overdueBatches)
        {
            // Already Retry status — just clear NextRetryTime so RetryJob re-schedules it
            b.NextRetryTime = null;
            retryRequeued++;
            logger.LogInformation("Recovery {Reason}: Batch {BatchId} overdue retry requeued",
                RecoveryReason.OverdueRetry, b.BatchId);
        }

        if (retryRequeued > 0) await db.SaveChangesAsync(ct);

        logger.LogInformation("SchedulerRecovery complete: sentRecovered={S} retryRequeued={R}",
            sentRecovered, retryRequeued);

        await mediator.Publish(new SchedulerRecoveryEvent(sentRecovered, retryRequeued), ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 5: Create `SyncJob`**

```csharp
// src/MSOSync.Scheduler/SyncJob.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Engine;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<SyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue<int>("Sync:IntervalSeconds", 30));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
            var engine       = scope.ServiceProvider.GetRequiredService<SyncEngine>();

            await using var lease = await lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
            if (lease == null)
            {
                logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
                continue;
            }

            try { await engine.RunAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "SyncJob run failed"); }
        }
    }
}
```

- [ ] **Step 6: Create `RetryJob`**

```csharp
// src/MSOSync.Scheduler/RetryJob.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class RetryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RetryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var scope        = scopeFactory.CreateAsyncScope();
            var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
            var processor    = scope.ServiceProvider.GetRequiredService<RetryProcessor>();

            await using var lease = await lockProvider.TryAcquireAsync(LockNames.RetryEngine, ct);
            if (lease == null) { logger.LogDebug("RetryJob: lock held, skipping"); continue; }

            try
            {
                var count = await processor.ProcessAsync(ct);
                if (count > 0) logger.LogInformation("RetryJob queued {Count} batches for retry", count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "RetryJob failed"); }
        }
    }
}
```

- [ ] **Step 7: Create `PurgeJob`**

Fires daily at 02:00 UTC. Uses `IClock` to compute the next fire time.

```csharp
// src/MSOSync.Scheduler/PurgeJob.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Event;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class PurgeJob(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<PurgeJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = TimeUntilNextFire();
            logger.LogDebug("PurgeJob sleeping {Delay} until next 02:00 UTC", delay);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }

            await RunPurgeAsync(ct);
        }
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        await using var scope        = scopeFactory.CreateAsyncScope();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
        var eventPurger  = scope.ServiceProvider.GetRequiredService<IEventPurger>();
        var batchPurger  = scope.ServiceProvider.GetRequiredService<BatchPurger>();

        await using var lease = await lockProvider.TryAcquireAsync(LockNames.PurgeEngine, ct);
        if (lease == null) { logger.LogDebug("PurgeJob: lock held, skipping"); return; }

        try
        {
            var events  = await eventPurger.PurgeAsync(ct);
            var batches = await batchPurger.PurgeAsync(ct);
            logger.LogInformation("PurgeJob: deleted {Events} events, {Batches} batches", events, batches);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { logger.LogError(ex, "PurgeJob failed"); }
    }

    private TimeSpan TimeUntilNextFire()
    {
        var now  = clock.UtcNow;
        var next = now.Date.AddHours(2);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
```

- [ ] **Step 8: Create `SyncSchedulerExtensions`**

`SchedulerRecovery` is registered first so its `StartAsync` completes before background jobs start ticking.

```csharp
// src/MSOSync.Scheduler/SyncSchedulerExtensions.cs
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());
        services.AddHostedService<SchedulerRecovery>();  // runs first
        services.AddHostedService<SyncJob>();
        services.AddHostedService<RetryJob>();
        services.AddHostedService<PurgeJob>();
        return services;
    }
}
```

- [ ] **Step 9: Delete `Placeholder.cs`**

```pwsh
Remove-Item src/MSOSync.Scheduler/Placeholder.cs
```

- [ ] **Step 10: Build**

```pwsh
dotnet build src/MSOSync.Scheduler/MSOSync.Scheduler.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 11: Commit**

```pwsh
git add src/MSOSync.Scheduler/MSOSync.Scheduler.csproj `
        src/MSOSync.Scheduler/RecoveryReason.cs `
        src/MSOSync.Scheduler/SchedulerRecoveryEvent.cs `
        src/MSOSync.Scheduler/SchedulerRecovery.cs `
        src/MSOSync.Scheduler/SyncJob.cs `
        src/MSOSync.Scheduler/RetryJob.cs `
        src/MSOSync.Scheduler/PurgeJob.cs `
        src/MSOSync.Scheduler/SyncSchedulerExtensions.cs
git rm src/MSOSync.Scheduler/Placeholder.cs
git commit -m "feat(scheduler): add SyncJob, RetryJob, PurgeJob, SchedulerRecovery"
```
