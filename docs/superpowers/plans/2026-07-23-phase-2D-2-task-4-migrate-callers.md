# Task 4: Migrate Callers + Update LockDto + Delete Old Files

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Swap `IDatabaseLockProvider` for `IDistributedLockService` in all four callers (`SyncJob`, `RetryJob`, `PurgeJob`, `BatchController`), expose `LockExpiry` in `LockDto` / `LockAdminService`, update the three scheduler test files to use the new interface, and delete the now-unused `IDatabaseLockProvider` / `DatabaseLockProvider` / `DatabaseLockLease` files.

**Prerequisite:** Tasks 1–3 complete. `IDistributedLockService`, `DistributedLockOptions`, `SqlDistributedLockService` are all registered and build cleanly.

**Files:**
- Modify: `src/MSOSync.Scheduler/SyncJob.cs`
- Modify: `src/MSOSync.Scheduler/RetryJob.cs`
- Modify: `src/MSOSync.Scheduler/PurgeJob.cs`
- Modify: `src/MSOSync.Api/Controllers/BatchController.cs`
- Modify: `src/MSOSync.Metadata/Locks/LockDto.cs`
- Modify: `src/MSOSync.Metadata/Locks/LockAdminService.cs`
- Modify: `tests/MSOSync.SchedulerTests/SyncJobTests.cs`
- Modify: `tests/MSOSync.SchedulerTests/RetryJobTests.cs`
- Modify: `tests/MSOSync.SchedulerTests/PurgeJobTests.cs`
- Delete: `src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs`
- Delete: `src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs`
- Delete: `src/MSOSync.Persistence/Lock/DatabaseLockLease.cs`

**Interfaces:**
- Consumes: `IDistributedLockService`, `IDistributedLock`, `DistributedLockOptions` from Tasks 1–2
- Produces: callers fully migrated; `IDatabaseLockProvider` removed from codebase

---

### Change pattern (applies to all four callers)

**Before** (example from SyncJob):
```csharp
using MSOSync.Persistence.Lock;
// ...
IDatabaseLockProvider lockProvider
// ...
var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
await using var lease = await lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
if (lease == null) { /* skip */ return; }
```

**After** (all four callers):
```csharp
using Microsoft.Extensions.Options;
using MSOSync.Common.Locks;
// ...
IDistributedLockService lockService
IOptions<DistributedLockOptions> lockOptions
// ...
var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();
var lockOptions = scope.ServiceProvider.GetRequiredService<IOptions<DistributedLockOptions>>();
var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
await using var handle = await lockService.TryAcquireAsync(
    LockNames.SyncEngine, owner, lockOptions.Value.DefaultExpiry, ct);
if (handle == null) { /* skip */ return; }
```

---

- [ ] **Step 1: Migrate `SyncJob.cs`**

Replace the entire content of `src/MSOSync.Scheduler/SyncJob.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory  scopeFactory,
    IOptions<SyncOptions> syncOptions,
    IWorkerStatusRegistry registry,
    ILogger<SyncJob>      logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(nameof(SyncJob), TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunTickAsync(ct);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        registry.RecordTickStart(nameof(SyncJob));
        try
        {
            await using var scope       = scopeFactory.CreateAsyncScope();
            var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();
            var lockOptions = scope.ServiceProvider.GetRequiredService<IOptions<DistributedLockOptions>>();
            var engine      = scope.ServiceProvider.GetRequiredService<SyncEngine>();

            var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
            await using var handle = await lockService.TryAcquireAsync(
                LockNames.SyncEngine, owner, lockOptions.Value.DefaultExpiry, ct);

            if (handle == null)
            {
                logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
                registry.RecordTickComplete(nameof(SyncJob));
                return;
            }

            await engine.RunAsync(ct);
            registry.RecordTickComplete(nameof(SyncJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(SyncJob), ex);
            logger.LogError(ex, "SyncJob run failed");
        }
    }
}
```

- [ ] **Step 2: Migrate `RetryJob.cs`**

Replace the entire content of `src/MSOSync.Scheduler/RetryJob.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class RetryJob(
    IServiceScopeFactory  scopeFactory,
    IWorkerStatusRegistry registry,
    ILogger<RetryJob>     logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // 5-minute fixed interval — retry cadence is not a tuneable operational parameter
        registry.Register(nameof(RetryJob), TimeSpan.FromMinutes(5));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunTickAsync(ct);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        registry.RecordTickStart(nameof(RetryJob));
        try
        {
            await using var scope       = scopeFactory.CreateAsyncScope();
            var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();
            var lockOptions = scope.ServiceProvider.GetRequiredService<IOptions<DistributedLockOptions>>();
            var processor   = scope.ServiceProvider.GetRequiredService<RetryProcessor>();

            var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
            await using var handle = await lockService.TryAcquireAsync(
                LockNames.RetryEngine, owner, lockOptions.Value.DefaultExpiry, ct);

            if (handle == null)
            {
                logger.LogDebug("RetryJob: lock held, skipping");
                registry.RecordTickComplete(nameof(RetryJob));
                return;
            }

            var count = await processor.ProcessAsync(ct);
            if (count > 0) logger.LogInformation("RetryJob queued {Count} batches for retry", count);
            registry.RecordTickComplete(nameof(RetryJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(RetryJob), ex);
            logger.LogError(ex, "RetryJob failed");
        }
    }
}
```

- [ ] **Step 3: Migrate `PurgeJob.cs`**

Replace the entire content of `src/MSOSync.Scheduler/PurgeJob.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Event;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class PurgeJob(
    IServiceScopeFactory  scopeFactory,
    IClock                clock,
    IWorkerStatusRegistry registry,
    ILogger<PurgeJob>     logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Task.Delay used intentionally — PurgeJob targets wall-clock 02:00 UTC, not a fixed interval
        registry.Register(nameof(PurgeJob), TimeSpan.FromHours(24));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = TimeUntilNextFire();
            logger.LogDebug("PurgeJob sleeping {Delay} until next 02:00 UTC", delay);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }

            registry.RecordTickStart(nameof(PurgeJob));
            try
            {
                await RunPurgeAsync(ct);
                registry.RecordTickComplete(nameof(PurgeJob));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(PurgeJob), ex);
                logger.LogError(ex, "PurgeJob failed");
            }
        }
    }

    internal async Task RunPurgeAsync(CancellationToken ct)
    {
        await using var scope       = scopeFactory.CreateAsyncScope();
        var lockService  = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();
        var lockOptions  = scope.ServiceProvider.GetRequiredService<IOptions<DistributedLockOptions>>();
        var eventPurger  = scope.ServiceProvider.GetRequiredService<IEventPurger>();
        var batchPurger  = scope.ServiceProvider.GetRequiredService<BatchPurger>();

        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
        await using var handle = await lockService.TryAcquireAsync(
            LockNames.PurgeEngine, owner, lockOptions.Value.DefaultExpiry, ct);

        if (handle == null) { logger.LogDebug("PurgeJob: lock held, skipping"); return; }

        var events  = await eventPurger.PurgeAsync(ct);
        var batches = await batchPurger.PurgeAsync(ct);
        logger.LogInformation("PurgeJob: deleted {Events} events, {Batches} batches", events, batches);
    }

    internal TimeSpan TimeUntilNextFire()
    {
        var now  = clock.UtcNow;
        var next = now.Date.AddHours(2);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
```

- [ ] **Step 4: Migrate `BatchController.cs`**

In `src/MSOSync.Api/Controllers/BatchController.cs`, make the following changes:

1. Replace `using MSOSync.Persistence.Lock;` with:
   ```csharp
   using Microsoft.Extensions.Options;
   using MSOSync.Common.Locks;
   using MSOSync.Persistence.Lock;
   ```
   (Keep `MSOSync.Persistence.Lock` for `LockNames`.)

2. Replace the constructor parameter `IDatabaseLockProvider lockProvider` with two parameters:
   ```csharp
   IDistributedLockService lockService,
   IOptions<DistributedLockOptions> lockOptions,
   ```

3. Update the `RetryAll` action body. Replace:
   ```csharp
   var lease = await lockProvider.TryAcquireAsync(LockNames.RetryEngine, ct);
   if (lease == null)
       return Conflict(new CodeMessageResponse(
           "LOCK_UNAVAILABLE", "Retry engine is currently running. Try again shortly."));

   await using (lease)
   {
       var count = await retryProcessor.ProcessAsync(ct);
       return Ok(new RetryAllResponse(count, DateTime.UtcNow, currentUser.GetCurrentUsername()));
   }
   ```
   with:
   ```csharp
   var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
   await using var handle = await lockService.TryAcquireAsync(
       LockNames.RetryEngine, owner, lockOptions.Value.DefaultExpiry, ct);
   if (handle == null)
       return Conflict(new CodeMessageResponse(
           "LOCK_UNAVAILABLE", "Retry engine is currently running. Try again shortly."));

   var count = await retryProcessor.ProcessAsync(ct);
   return Ok(new RetryAllResponse(count, DateTime.UtcNow, currentUser.GetCurrentUsername()));
   ```

Full updated `BatchController.cs`:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Batches;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Validators;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.OutgoingBatches;
using MSOSync.Persistence.Lock;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/batches")]
public sealed class BatchController(
    IOutgoingBatchQueryService              batchQuery,
    IBatchStateMachine                      stateMachine,
    RetryProcessor                          retryProcessor,
    ICurrentUserService                     currentUser,
    IDistributedLockService                 lockService,
    IOptions<DistributedLockOptions>        lockOptions,
    IExportService<OutgoingBatchExportFilter> exporter,
    IExportAuditService                     exportAudit,
    OutgoingBatchExportFilterValidator      exportFilterValidator) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<OutgoingBatchDto>), 200)]
    public async Task<IActionResult> GetBatches([FromQuery] BatchListRequest req, CancellationToken ct)
    {
        byte? status = null;
        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<BatchStatus>(req.Status, ignoreCase: true, out var parsed))
            status = (byte)parsed;

        var page = await batchQuery.GetBatchesAsync(new OutgoingBatchQueryFilter(
            req.NodeId, req.ChannelId, status, req.SortBy, req.SortDirection, req.Page, req.PageSize), ct);

        var totalPages = (int)Math.Ceiling(page.Total / (double)req.PageSize);
        var data = page.Items.Select(b => new OutgoingBatchDto(
            b.BatchId, (BatchStatus)b.Status, b.NodeId, b.ChannelId,
            b.CreateTime, b.SentTime, b.AckTime, b.RetryCount, b.RowCount, b.LatestError));

        return Ok(new PagedResponse<OutgoingBatchDto>(data, page.Total, req.Page, req.PageSize, totalPages));
    }

    [HttpGet("{batchId:long}")]
    [Authorize]
    [ProducesResponseType(typeof(OutgoingBatchDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetBatch(long batchId, CancellationToken ct)
    {
        var batch = await batchQuery.GetBatchByIdAsync(batchId, ct);
        if (batch is null) return NotFound();

        var dto = new OutgoingBatchDto(
            batch.BatchId, (BatchStatus)batch.Status, batch.NodeId, batch.ChannelId,
            batch.CreateTime, batch.SentTime, batch.AckTime, batch.RetryCount, batch.RowCount, batch.LatestError);

        return Ok(dto);
    }

    [HttpPost("{batchId:long}/retry")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(CodeMessageResponse), 409)]
    public async Task<IActionResult> RetryBatch(long batchId, CancellationToken ct)
    {
        var batch = await batchQuery.GetBatchByIdAsync(batchId, ct);
        if (batch is null) return NotFound();

        var transitioned = await stateMachine.MoveToRetryAsync(batchId, ct);

        if (!transitioned)
            return Conflict(new CodeMessageResponse(
                "INVALID_TRANSITION", $"Batch {batchId} is not in Error status"));

        return Ok();
    }

    [HttpPost("retry-all")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(RetryAllResponse), 200)]
    [ProducesResponseType(typeof(CodeMessageResponse), 409)]
    public async Task<IActionResult> RetryAll(CancellationToken ct)
    {
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
        await using var handle = await lockService.TryAcquireAsync(
            LockNames.RetryEngine, owner, lockOptions.Value.DefaultExpiry, ct);

        if (handle == null)
            return Conflict(new CodeMessageResponse(
                "LOCK_UNAVAILABLE", "Retry engine is currently running. Try again shortly."));

        var count = await retryProcessor.ProcessAsync(ct);
        return Ok(new RetryAllResponse(count, DateTime.UtcNow, currentUser.GetCurrentUsername()));
    }

    [HttpGet("export")]
    [Authorize(Policy = "ViewerOrAbove")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> ExportBatches(
        [FromQuery] OutgoingBatchExportFilter filter,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        await exportFilterValidator.ValidateAndThrowAsync(filter, ct);

        var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return new MSOSync.Api.Results.StreamingExportResult(
            isJson
                ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
                : (s, t) => exporter.ExportCsvAsync(s, filter, t),
            isJson ? "application/json" : "text/csv",
            isJson ? $"batches-{date}.json" : $"batches-{date}.csv",
            (rows, ms) => exportAudit.WriteAsync("outgoing-batches", format, rows, ms, ct),
            ct);
    }
}
```

- [ ] **Step 5: Update `LockDto.cs` to expose `LockExpiry`**

Replace the content of `src/MSOSync.Metadata/Locks/LockDto.cs` with:

```csharp
namespace MSOSync.Metadata.Locks;

public sealed record LockDto(
    string    LockName,
    string?   LockOwner,
    DateTime? LockTime,
    DateTime? LockExpiry);
```

- [ ] **Step 6: Update `LockAdminService.GetLocksAsync` to project `LockExpiry`**

Edit `src/MSOSync.Metadata/Locks/LockAdminService.cs`. Replace the `Select` projection in `GetLocksAsync`:

```csharp
// Before:
.Select(l => new LockDto(l.LockName, l.LockOwner, l.LockTime))

// After:
.Select(l => new LockDto(l.LockName, l.LockOwner, l.LockTime, l.LockExpiry))
```

Full updated file:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Locks;

public sealed class LockAdminService(AppDbContext db) : ILockAdminService
{
    public async Task<IReadOnlyList<LockDto>> GetLocksAsync(CancellationToken ct = default)
    {
        return await db.Locks.AsNoTracking()
            .OrderBy(l => l.LockName)
            .Select(l => new LockDto(l.LockName, l.LockOwner, l.LockTime, l.LockExpiry))
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteLockAsync(string lockName, CancellationToken ct = default)
    {
        var entity = await db.Locks
            .FirstOrDefaultAsync(l => l.LockName == lockName, ct);

        if (entity is null) return false;

        db.Locks.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 7: Build solution to confirm compile-time correctness before updating tests**

```
dotnet build MSOSync.sln
```

Expected: errors in `tests/MSOSync.SchedulerTests/` (still reference `IDatabaseLockProvider`) — this is expected. All other projects should compile clean.

- [ ] **Step 8: Update `SyncJobTests.cs`**

Replace the entire content of `tests/MSOSync.SchedulerTests/SyncJobTests.cs` with:

```csharp
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Lock;
using MSOSync.Routing;
using MSOSync.Scheduler;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SyncJobTests
{
    private readonly Mock<IDistributedLockService>   _lockService = new();
    private readonly Mock<IDistributedLock>           _lockHandle  = new();
    private readonly Mock<IWorkerStatusRegistry>      _registry    = new();
    private readonly Mock<ITriggerDriftDetector>      _driftDetector = new();
    private readonly Mock<IEventReader>               _eventReader   = new();
    private readonly Mock<IRoutingService>            _routing       = new();
    private readonly Mock<IBatchCreator>              _batchCreator  = new();
    private readonly Mock<ITransportService>          _transport     = new();
    private readonly Mock<IMediator>                  _mediator      = new();
    private readonly Mock<IClock>                     _clock         = new();

    private SyncJob BuildJob()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _lockService.Object);
        services.AddSingleton<IOptions<DistributedLockOptions>>(
            Options.Create(new DistributedLockOptions { DefaultExpiry = TimeSpan.FromSeconds(30) }));
        services.AddScoped(_ => new SyncEngine(
            _driftDetector.Object, _eventReader.Object, _routing.Object,
            _batchCreator.Object, _transport.Object, _mediator.Object,
            _clock.Object, NullLogger<SyncEngine>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new SyncJob(
            scopeFactory,
            Options.Create(new SyncOptions()),
            _registry.Object,
            NullLogger<SyncJob>.Instance);
    }

    [Fact]
    public async Task RunTick_skips_engine_when_lock_not_acquired()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.SyncEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(x => x.RecordTickStart(nameof(SyncJob), TickTrigger.Scheduled), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_runs_engine_when_lock_acquired()
    {
        _lockHandle.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.SyncEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_lockHandle.Object);
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SyncDataEvent>());

        await BuildJob().RunTickAsync(CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_records_failure_when_engine_throws()
    {
        _lockHandle.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.SyncEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_lockHandle.Object);
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(
            x => x.RecordTickFailed(nameof(SyncJob), It.IsAny<InvalidOperationException>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Never);
    }
}
```

- [ ] **Step 9: Update `RetryJobTests.cs`**

Replace the entire content of `tests/MSOSync.SchedulerTests/RetryJobTests.cs` with:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class RetryJobTests
{
    private readonly Mock<IDistributedLockService> _lockService = new();
    private readonly Mock<IDistributedLock>        _lockHandle  = new();
    private readonly Mock<IWorkerStatusRegistry>   _registry    = new();
    private readonly Mock<IClock>                  _clock       = new();

    private RetryJob BuildJob()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => _lockService.Object);
        services.AddSingleton<IOptions<DistributedLockOptions>>(
            Options.Create(new DistributedLockOptions { DefaultExpiry = TimeSpan.FromSeconds(30) }));
        services.AddScoped(_ => new RetryProcessor(
            new AppDbContext(dbOptions), _clock.Object, NullLogger<RetryProcessor>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new RetryJob(scopeFactory, _registry.Object, NullLogger<RetryJob>.Instance);
    }

    [Fact]
    public async Task RunTick_skips_processor_when_lock_not_acquired()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.RetryEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(x => x.RecordTickStart(nameof(RetryJob), TickTrigger.Scheduled), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_completes_when_no_retry_candidates()
    {
        _lockHandle.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.RetryEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_lockHandle.Object);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_records_failure_when_lock_service_throws()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.RetryEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(
            x => x.RecordTickFailed(nameof(RetryJob), It.IsAny<InvalidOperationException>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(RetryJob)), Times.Never);
    }
}
```

- [ ] **Step 10: Update `PurgeJobTests.cs`**

Replace the entire content of `tests/MSOSync.SchedulerTests/PurgeJobTests.cs` with:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Common.Workers;
using MSOSync.Event;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class PurgeJobTests
{
    private readonly Mock<IDistributedLockService> _lockService = new();
    private readonly Mock<IDistributedLock>        _lockHandle  = new();
    private readonly Mock<IWorkerStatusRegistry>   _registry    = new();
    private readonly Mock<IEventPurger>            _eventPurger = new();
    private readonly Mock<IClock>                  _clock       = new();

    private PurgeJob BuildJob()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => _lockService.Object);
        services.AddSingleton<IOptions<DistributedLockOptions>>(
            Options.Create(new DistributedLockOptions { DefaultExpiry = TimeSpan.FromSeconds(30) }));
        services.AddScoped(_ => _eventPurger.Object);
        services.AddScoped(_ => new BatchPurger(
            new AppDbContext(dbOptions), _clock.Object, NullLogger<BatchPurger>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new PurgeJob(
            scopeFactory, _clock.Object, _registry.Object, NullLogger<PurgeJob>.Instance);
    }

    [Fact]
    public async Task RunPurge_skips_purgers_when_lock_not_acquired()
    {
        _lockService
            .Setup(x => x.TryAcquireAsync(
                LockNames.PurgeEngine,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        await BuildJob().RunPurgeAsync(CancellationToken.None);

        _eventPurger.Verify(x => x.PurgeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TimeUntilNextFire_targets_today_when_before_0200_utc()
    {
        _clock.Setup(x => x.UtcNow).Returns(new DateTime(2026, 7, 21, 0, 30, 0, DateTimeKind.Utc));

        var delay = BuildJob().TimeUntilNextFire();

        delay.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void TimeUntilNextFire_targets_tomorrow_when_after_0200_utc()
    {
        _clock.Setup(x => x.UtcNow).Returns(new DateTime(2026, 7, 21, 3, 0, 0, DateTimeKind.Utc));

        var delay = BuildJob().TimeUntilNextFire();

        delay.Should().Be(TimeSpan.FromHours(23));
    }
}
```

- [ ] **Step 11: Delete the old lock files**

```
git rm src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs
git rm src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs
git rm src/MSOSync.Persistence/Lock/DatabaseLockLease.cs
```

- [ ] **Step 12: Verify no remaining references to IDatabaseLockProvider**

Run a search across the src/ directory:

```
dotnet build MSOSync.sln
```

If there are any remaining references to `IDatabaseLockProvider` or `DatabaseLockProvider`, the build will fail with CS0246. Fix any that appear.

To manually check:

```
grep -r "IDatabaseLockProvider\|DatabaseLockProvider\|DatabaseLockLease" src/ --include="*.cs"
```

Expected: zero results.

- [ ] **Step 13: Run all scheduler tests**

```
dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj -v minimal
```

Expected: all existing tests pass (SyncJobTests: 3, RetryJobTests: 3, PurgeJobTests: 3, plus any others in the project).

- [ ] **Step 14: Run full test suite**

```
dotnet test MSOSync.sln -v minimal
```

Expected: 0 failures. Any previously-passing tests continue to pass.

- [ ] **Step 15: Commit**

```
git add src/MSOSync.Scheduler/SyncJob.cs
git add src/MSOSync.Scheduler/RetryJob.cs
git add src/MSOSync.Scheduler/PurgeJob.cs
git add src/MSOSync.Api/Controllers/BatchController.cs
git add src/MSOSync.Metadata/Locks/LockDto.cs
git add src/MSOSync.Metadata/Locks/LockAdminService.cs
git add tests/MSOSync.SchedulerTests/SyncJobTests.cs
git add tests/MSOSync.SchedulerTests/RetryJobTests.cs
git add tests/MSOSync.SchedulerTests/PurgeJobTests.cs
git commit -m "feat(2D.2-T4): migrate all callers to IDistributedLockService, expose LockExpiry in LockDto, delete IDatabaseLockProvider"
```
