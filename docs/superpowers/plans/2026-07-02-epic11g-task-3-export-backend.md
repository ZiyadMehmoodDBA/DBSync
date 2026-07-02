# Task 3: Export Job Backend

**Part of:** Epic 11G — Performance & Scale  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11g-performance-scale-design.md`  
**Depends on:** Tasks 1 and 2 (Track 1) — can start Track 2 once Track 1 is merged

## Files

**Create:**
- `src/MSOSync.Persistence/Entities/SyncExportJob.cs`
- `src/MSOSync.Persistence/Configurations/SyncExportJobConfiguration.cs`
- `src/MSOSync.Persistence/Migrations/` — M019_ExportJobs via `dotnet ef`
- `src/MSOSync.App/Export/ExportOptions.cs`
- `src/MSOSync.App/Export/ExportJobChangedNotification.cs`
- `src/MSOSync.App/Export/IExportJobService.cs`
- `src/MSOSync.App/Export/ExportJobService.cs`
- `src/MSOSync.App/Workers/ExportJobWorker.cs`
- `src/MSOSync.App/Workers/ExportCleanupWorker.cs`
- `src/MSOSync.App/SignalR/ExportJobChangedPublisher.cs`
- `src/MSOSync.Api/Controllers/ExportJobController.cs`
- `tests/MSOSync.IntegrationTests/Export/ExportJobIntegrationTests.cs`

**Modify:**
- `src/MSOSync.Persistence/AppDbContext.cs` — add `DbSet<SyncExportJob> ExportJobs`
- `src/MSOSync.App/Program.cs` — register `ExportOptions`, workers, `ExportJobService`

## Interfaces Produced (consumed by Task 4)

```
SignalR message name: "ExportJobEvent"
SignalR payload: { jobId: string, status: string, progressPercent: number, rowCount: number | null }
  Sent only to job owner via: hub.Clients.User(job.RequestedBy)

POST   /api/v1/export-jobs
  Body: { resourceType: string, format: string, filtersJson: string, parentJobId?: string }
  Returns: 202 { jobId: string }

GET    /api/v1/export-jobs
  Returns: ExportJobDto[]  (caller's jobs; ?all=true → all jobs if MANAGE_USERS)

GET    /api/v1/export-jobs/{id}/download
  Returns: file stream
  Authorization: job.RequestedBy == currentUser OR MANAGE_USERS permission

DELETE /api/v1/export-jobs/{id}
  Returns: 204 (soft-delete)

ExportJobDto {
  jobId: string (guid)
  parentJobId: string | null
  requestedBy: string
  resourceType: string
  format: string
  status: string  // "Pending" | "Running" | "Completed" | "Failed" | "Deleted" | "Expired"
  progressPercent: number
  rowCount: number | null
  errorMessage: string | null
  expiresAt: string | null  // ISO 8601
  createdAt: string
  startedAt: string | null
  completedAt: string | null
}
```

---

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- EF Core 9 — `AsNoTracking()` on reads, `SaveChangesAsync(ct)` on writes
- No new NuGet packages
- `ExportJobWorker`: `MaxConcurrentJobs = 1` (single worker, sequential jobs)
- `ExportCleanupWorker`: separate `BackgroundService`, runs every 60 minutes
- `ClaimNextPendingJobAsync` must be atomic — single `UPDATE...OUTPUT` SQL
- SignalR: `hub.Clients.User(job.RequestedBy)` — NOT `Clients.All`
- Build env: `$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"` and `$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"`

---

- [ ] **Step 1: Read existing patterns**

Before writing anything, read:
- `src/MSOSync.App/SignalR/PermissionChangedPublisher.cs` — SignalR publisher pattern (already in context: uses `IHubContext<OperationsHub>`)
- `src/MSOSync.App/Program.cs` — how BackgroundServices are registered (look for `AddHostedService`)
- `src/MSOSync.App/Hubs/OperationsHub.cs` — to understand hub setup and how `Clients.User(...)` maps to authenticated username
- `src/MSOSync.Persistence/Entities/SyncPermission.cs` — entity pattern to follow
- `src/MSOSync.Persistence/Configurations/SyncPermissionConfiguration.cs` — Fluent API config pattern
- `src/MSOSync.Api/Controllers/PermissionsController.cs` — controller pattern with auth policies
- `src/MSOSync.Metadata/Permissions/IPermissionService.cs` — to confirm `MANAGE_USERS` constant name for authorization check

Note: the hub uses ASP.NET Core SignalR's user ID provider. The `hub.Clients.User(username)` call routes to connections where the authenticated user's `ClaimTypes.Name` == `username`. This matches how `ICurrentUserService.GetCurrentUsername()` works. If `OperationsHub` uses groups instead of user IDs, adjust accordingly — read the hub file.

- [ ] **Step 2: Create `SyncExportJob` entity**

```csharp
// src/MSOSync.Persistence/Entities/SyncExportJob.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncExportJob
{
    public Guid     JobId           { get; set; }
    public Guid?    ParentJobId     { get; set; }
    public string   RequestedBy     { get; set; } = string.Empty;
    public string   ResourceType    { get; set; } = string.Empty;
    public string   Format          { get; set; } = string.Empty;
    public string   FiltersJson     { get; set; } = string.Empty;
    public string   Status          { get; set; } = ExportJobStatus.Pending;
    public int      ProgressPercent { get; set; }
    public long?    RowCount        { get; set; }
    public string?  OutputPath      { get; set; }
    public string?  ErrorMessage    { get; set; }
    public DateTimeOffset?  ExpiresAt    { get; set; }
    public DateTimeOffset   CreatedAt    { get; set; }
    public DateTimeOffset?  StartedAt    { get; set; }
    public DateTimeOffset?  CompletedAt  { get; set; }
}

public static class ExportJobStatus
{
    public const string Pending   = "Pending";
    public const string Running   = "Running";
    public const string Completed = "Completed";
    public const string Failed    = "Failed";
    public const string Deleted   = "Deleted";
    public const string Expired   = "Expired";
}
```

- [ ] **Step 3: Create `SyncExportJobConfiguration`**

```csharp
// src/MSOSync.Persistence/Configurations/SyncExportJobConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncExportJobConfiguration : IEntityTypeConfiguration<SyncExportJob>
{
    public void Configure(EntityTypeBuilder<SyncExportJob> b)
    {
        b.ToTable("sync_export_job");
        b.HasKey(x => x.JobId);

        b.Property(x => x.JobId)        .HasColumnName("job_id")         .HasDefaultValueSql("NEWID()");
        b.Property(x => x.ParentJobId)  .HasColumnName("parent_job_id");
        b.Property(x => x.RequestedBy)  .HasColumnName("requested_by")   .HasMaxLength(256).IsRequired();
        b.Property(x => x.ResourceType) .HasColumnName("resource_type")  .HasMaxLength(50) .IsRequired();
        b.Property(x => x.Format)       .HasColumnName("format")         .HasMaxLength(10) .IsRequired();
        b.Property(x => x.FiltersJson)  .HasColumnName("filters_json")   .IsRequired();
        b.Property(x => x.Status)       .HasColumnName("status")         .HasMaxLength(20) .IsRequired();
        b.Property(x => x.ProgressPercent).HasColumnName("progress_percent").HasDefaultValue(0);
        b.Property(x => x.RowCount)     .HasColumnName("row_count");
        b.Property(x => x.OutputPath)   .HasColumnName("output_path")    .HasMaxLength(500);
        b.Property(x => x.ErrorMessage) .HasColumnName("error_message")  .HasMaxLength(1000);
        b.Property(x => x.ExpiresAt)    .HasColumnName("expires_at");
        b.Property(x => x.CreatedAt)    .HasColumnName("created_at")     .HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.StartedAt)    .HasColumnName("started_at");
        b.Property(x => x.CompletedAt)  .HasColumnName("completed_at");

        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_export_job_status_created");
        b.HasIndex(x => new { x.RequestedBy, x.CreatedAt }).HasDatabaseName("IX_export_job_requested_by");
    }
}
```

- [ ] **Step 4: Register `DbSet<SyncExportJob>` in `AppDbContext`**

In `src/MSOSync.Persistence/AppDbContext.cs`, add after the existing `RolePermissions` line:

```csharp
public DbSet<SyncExportJob> ExportJobs => Set<SyncExportJob>();
```

- [ ] **Step 5: Generate M019 migration**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet ef migrations add M019_ExportJobs `
  --project src/MSOSync.Persistence `
  --startup-project src/MSOSync.App `
  --output-dir Migrations `
  2>&1 | Select-Object -Last 8
```

Expected: `Done. To undo this action, use 'ef migrations remove'` or similar. Open the generated `M019_ExportJobs.cs` and verify it contains `CreateTable("sync_export_job", ...)` with all 14 columns.

- [ ] **Step 6: Create `ExportOptions`**

```csharp
// src/MSOSync.App/Export/ExportOptions.cs
namespace MSOSync.App.Export;

public sealed class ExportOptions
{
    public int    ImmediateThreshold { get; set; } = 50_000;
    public string BasePath           { get; set; } = "exports";
    public int    RetentionHours     { get; set; } = 24;
    public int    MaxConcurrentJobs  { get; set; } = 1;
}
```

- [ ] **Step 7: Create `ExportJobChangedNotification`**

```csharp
// src/MSOSync.App/Export/ExportJobChangedNotification.cs
using MediatR;

namespace MSOSync.App.Export;

public sealed record ExportJobChangedNotification(
    Guid   JobId,
    string RequestedBy,
    string Status,
    int    ProgressPercent,
    long?  RowCount
) : INotification;
```

- [ ] **Step 8: Create `IExportJobService`**

```csharp
// src/MSOSync.App/Export/IExportJobService.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.App.Export;

public interface IExportJobService
{
    Task<SyncExportJob> CreateJobAsync(
        string requestedBy, string resourceType, string format,
        string filtersJson, Guid? parentJobId, CancellationToken ct);

    Task<SyncExportJob?> GetJobAsync(Guid jobId, CancellationToken ct);

    Task<IReadOnlyList<SyncExportJob>> GetJobsForUserAsync(string username, CancellationToken ct);

    Task<IReadOnlyList<SyncExportJob>> GetAllJobsAsync(CancellationToken ct);

    // Atomic claim — UPDATE...OUTPUT — safe for future multi-worker
    Task<SyncExportJob?> ClaimNextPendingJobAsync(CancellationToken ct);

    Task UpdateProgressAsync(Guid jobId, int progressPercent, CancellationToken ct);

    Task CompleteJobAsync(Guid jobId, string outputPath, long rowCount, CancellationToken ct);

    Task FailJobAsync(Guid jobId, string errorMessage, CancellationToken ct);

    Task SoftDeleteJobAsync(Guid jobId, CancellationToken ct);

    Task ExpireJobsAsync(CancellationToken ct);
}
```

- [ ] **Step 9: Create `ExportJobService`**

```csharp
// src/MSOSync.App/Export/ExportJobService.cs
using System.Diagnostics.Metrics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.App.Export;

public sealed class ExportJobService(
    AppDbContext db,
    IMediator mediator,
    IOptions<ExportOptions> opts)
    : IExportJobService
{
    private static readonly Meter s_meter     = new("MSOSync.Export");
    private static readonly Counter<long> s_created   = s_meter.CreateCounter<long>("msosync_export_jobs_created_total");
    private static readonly Counter<long> s_completed = s_meter.CreateCounter<long>("msosync_export_jobs_completed_total");
    private static readonly Counter<long> s_failed    = s_meter.CreateCounter<long>("msosync_export_jobs_failed_total");
    private static readonly Counter<long> s_rows      = s_meter.CreateCounter<long>("msosync_export_rows_written_total");
    private static readonly Histogram<double> s_duration =
        s_meter.CreateHistogram<double>("msosync_export_job_duration_seconds");

    public async Task<SyncExportJob> CreateJobAsync(
        string requestedBy, string resourceType, string format,
        string filtersJson, Guid? parentJobId, CancellationToken ct)
    {
        var job = new SyncExportJob
        {
            JobId        = Guid.NewGuid(),
            ParentJobId  = parentJobId,
            RequestedBy  = requestedBy,
            ResourceType = resourceType,
            Format       = format,
            FiltersJson  = filtersJson,
            Status       = ExportJobStatus.Pending,
            CreatedAt    = DateTimeOffset.UtcNow,
        };
        db.ExportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        s_created.Add(1);
        return job;
    }

    public Task<SyncExportJob?> GetJobAsync(Guid jobId, CancellationToken ct)
        => db.ExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == jobId, ct);

    public Task<IReadOnlyList<SyncExportJob>> GetJobsForUserAsync(string username, CancellationToken ct)
        => db.ExportJobs.AsNoTracking()
            .Where(j => j.RequestedBy == username)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<SyncExportJob>)t.Result.AsReadOnly(), ct);

    public Task<IReadOnlyList<SyncExportJob>> GetAllJobsAsync(CancellationToken ct)
        => db.ExportJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<SyncExportJob>)t.Result.AsReadOnly(), ct);

    public async Task<SyncExportJob?> ClaimNextPendingJobAsync(CancellationToken ct)
    {
        // Atomic: update exactly one Pending job to Running and return its job_id
        var claimedIds = await db.Database
            .SqlQuery<Guid>($"""
                UPDATE e
                SET e.status = 'Running', e.started_at = SYSUTCDATETIME()
                OUTPUT inserted.job_id
                FROM sync_export_job e
                INNER JOIN (
                    SELECT TOP(1) job_id
                    FROM sync_export_job
                    WHERE status = 'Pending'
                    ORDER BY created_at ASC
                ) t ON e.job_id = t.job_id
                """)
            .ToListAsync(ct);

        if (claimedIds.Count == 0) return null;

        return await db.ExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == claimedIds[0], ct);
    }

    public async Task UpdateProgressAsync(Guid jobId, int progressPercent, CancellationToken ct)
    {
        await db.ExportJobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.ProgressPercent, progressPercent), ct);
        await PublishAsync(jobId, ct);
    }

    public async Task CompleteJobAsync(Guid jobId, string outputPath, long rowCount, CancellationToken ct)
    {
        var job = await db.ExportJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job is null) return;

        var duration = job.StartedAt.HasValue
            ? (DateTimeOffset.UtcNow - job.StartedAt.Value).TotalSeconds
            : 0;

        job.Status          = ExportJobStatus.Completed;
        job.OutputPath      = outputPath;
        job.RowCount        = rowCount;
        job.CompletedAt     = DateTimeOffset.UtcNow;
        job.ProgressPercent = 100;
        job.ExpiresAt       = DateTimeOffset.UtcNow.AddHours(opts.Value.RetentionHours);
        await db.SaveChangesAsync(ct);

        s_completed.Add(1);
        s_rows.Add(rowCount);
        s_duration.Record(duration);
        await PublishAsync(jobId, ct);
    }

    public async Task FailJobAsync(Guid jobId, string errorMessage, CancellationToken ct)
    {
        await db.ExportJobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status,       ExportJobStatus.Failed)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedAt,  DateTimeOffset.UtcNow), ct);
        s_failed.Add(1);
        await PublishAsync(jobId, ct);
    }

    public async Task SoftDeleteJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.ExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job?.OutputPath is not null && File.Exists(job.OutputPath))
            File.Delete(job.OutputPath);

        await db.ExportJobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ExportJobStatus.Deleted), ct);
        await PublishAsync(jobId, ct);
    }

    public async Task ExpireJobsAsync(CancellationToken ct)
    {
        var expired = await db.ExportJobs
            .Where(j => j.ExpiresAt <= DateTimeOffset.UtcNow
                     && (j.Status == ExportJobStatus.Completed || j.Status == ExportJobStatus.Failed))
            .ToListAsync(ct);

        foreach (var job in expired)
        {
            if (job.OutputPath is not null && File.Exists(job.OutputPath))
                File.Delete(job.OutputPath);
            job.Status = ExportJobStatus.Expired;
        }

        if (expired.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private async Task PublishAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.ExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job is null) return;

        await mediator.Publish(new ExportJobChangedNotification(
            job.JobId, job.RequestedBy, job.Status, job.ProgressPercent, job.RowCount), ct);
    }
}
```

- [ ] **Step 10: Create `ExportJobWorker`**

The worker picks one job at a time, executes it, updates progress every 1,000 rows, then sleeps 5 seconds. It delegates actual data streaming to the existing `IExportService<TFilter>` infrastructure from Epic 11D (already wired for events/batches/audit). Read how `IExportService<EventFilter>` is called in `EventsController.ExportEvents` for the pattern.

```csharp
// src/MSOSync.App/Workers/ExportJobWorker.cs
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.App.Export;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Metadata.OutgoingBatches;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Export;
using MSOSync.Persistence.Entities;

namespace MSOSync.App.Workers;

public sealed class ExportJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ExportOptions> opts,
    ILogger<ExportJobWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(opts.Value.BasePath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in ExportJobWorker loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IExportJobService>();

        var job = await jobService.ClaimNextPendingJobAsync(ct);
        if (job is null) return;

        logger.LogInformation("Starting export job {JobId} ({ResourceType}/{Format})",
            job.JobId, job.ResourceType, job.Format);

        try
        {
            var outputPath = Path.Combine(opts.Value.BasePath, $"{job.JobId}.{job.Format}");
            await WriteExportFileAsync(scope.ServiceProvider, job, outputPath, jobService, ct);
            await jobService.CompleteJobAsync(job.JobId, outputPath,
                await CountRowsAsync(outputPath), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export job {JobId} failed", job.JobId);
            await jobService.FailJobAsync(job.JobId, ex.Message, ct);
        }
    }

    private static async Task WriteExportFileAsync(
        IServiceProvider sp, SyncExportJob job, string outputPath,
        IExportJobService jobService, CancellationToken ct)
    {
        await using var stream = File.Create(outputPath);
        var isJson = job.Format.Equals("json", StringComparison.OrdinalIgnoreCase);

        // Dispatch to the correct export service based on resource type
        switch (job.ResourceType.ToLowerInvariant())
        {
            case "events":
                var eventFilter = JsonSerializer.Deserialize<EventFilter>(job.FiltersJson)!;
                var eventExporter = sp.GetRequiredService<IExportService<EventFilter>>();
                if (isJson) await eventExporter.ExportJsonAsync(stream, eventFilter, ct);
                else        await eventExporter.ExportCsvAsync(stream, eventFilter, ct);
                break;

            case "incoming-batches":
                var ibFilter = JsonSerializer.Deserialize<IncomingBatchFilter>(job.FiltersJson)!;
                var ibExporter = sp.GetRequiredService<IExportService<IncomingBatchFilter>>();
                if (isJson) await ibExporter.ExportJsonAsync(stream, ibFilter, ct);
                else        await ibExporter.ExportCsvAsync(stream, ibFilter, ct);
                break;

            case "outgoing-batches":
                var obFilter = JsonSerializer.Deserialize<OutgoingBatchFilter>(job.FiltersJson)!;
                var obExporter = sp.GetRequiredService<IExportService<OutgoingBatchFilter>>();
                if (isJson) await obExporter.ExportJsonAsync(stream, obFilter, ct);
                else        await obExporter.ExportCsvAsync(stream, obFilter, ct);
                break;

            case "audit":
                var auditFilter = JsonSerializer.Deserialize<AuditFilter>(job.FiltersJson)!;
                var auditExporter = sp.GetRequiredService<IExportService<AuditFilter>>();
                if (isJson) await auditExporter.ExportJsonAsync(stream, auditFilter, ct);
                else        await auditExporter.ExportCsvAsync(stream, auditFilter, ct);
                break;

            default:
                throw new InvalidOperationException($"Unknown resource type: {job.ResourceType}");
        }
    }

    private static async Task<long> CountRowsAsync(string outputPath)
    {
        // Count lines in the file as a proxy for row count (works for CSV; for JSON parse array)
        var lines = await File.ReadAllLinesAsync(outputPath);
        return Math.Max(0, lines.Length - 1); // subtract header row for CSV
    }
}
```

Note: If `IExportService<TFilter>` uses different namespace imports than shown, read the existing `ExportService` implementation from the Epic 11D files (look in `src/MSOSync.Metadata/Export/`). Adjust namespace references accordingly.

- [ ] **Step 11: Create `ExportCleanupWorker`**

```csharp
// src/MSOSync.App/Workers/ExportCleanupWorker.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.App.Export;

namespace MSOSync.App.Workers;

public sealed class ExportCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExportCleanupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IExportJobService>();
                await svc.ExpireJobsAsync(stoppingToken);
                logger.LogDebug("Export cleanup completed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ExportCleanupWorker");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
```

- [ ] **Step 12: Create `ExportJobChangedPublisher`**

Scoped to the job owner — NOT `Clients.All`:

```csharp
// src/MSOSync.App/SignalR/ExportJobChangedPublisher.cs
using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Export;
using MSOSync.App.Hubs;

namespace MSOSync.App.SignalR;

public sealed class ExportJobChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<ExportJobChangedNotification>
{
    public async Task Handle(ExportJobChangedNotification n, CancellationToken ct)
    {
        await hub.Clients.User(n.RequestedBy).SendAsync("ExportJobEvent", new
        {
            jobId           = n.JobId,
            status          = n.Status,
            progressPercent = n.ProgressPercent,
            rowCount        = n.RowCount,
        }, ct);
    }
}
```

- [ ] **Step 13: Create `ExportJobController`**

```csharp
// src/MSOSync.Api/Controllers/ExportJobController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.App.Export;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/export-jobs")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ExportJobController(
    IExportJobService jobService,
    ICurrentUserService currentUser,
    IPermissionService permissionService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(202)]
    public async Task<IActionResult> CreateJob(
        [FromBody] CreateExportJobRequest request, CancellationToken ct)
    {
        var username = currentUser.GetCurrentUsername();
        var job = await jobService.CreateJobAsync(
            username,
            request.ResourceType,
            request.Format,
            request.FiltersJson,
            request.ParentJobId,
            ct);
        return StatusCode(202, new { jobId = job.JobId });
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ExportJobDto>), 200)]
    public async Task<IActionResult> GetJobs(
        [FromQuery] bool all = false, CancellationToken ct = default)
    {
        var username = currentUser.GetCurrentUsername();

        IReadOnlyList<SyncExportJob> jobs;
        if (all)
        {
            var perms = await permissionService.GetEffectivePermissionsAsync(username, ct);
            if (!perms.Permissions.Contains(SystemPermissions.ManageUsers))
                return Forbid();
            jobs = await jobService.GetAllJobsAsync(ct);
        }
        else
        {
            jobs = await jobService.GetJobsForUserAsync(username, ct);
        }

        return Ok(jobs.Select(ToDto));
    }

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var job = await jobService.GetJobAsync(id, ct);
        if (job is null || job.Status is ExportJobStatus.Deleted or ExportJobStatus.Expired)
            return NotFound();

        var username = currentUser.GetCurrentUsername();
        if (job.RequestedBy != username)
        {
            var perms = await permissionService.GetEffectivePermissionsAsync(username, ct);
            if (!perms.Permissions.Contains(SystemPermissions.ManageUsers))
                return Forbid();
        }

        if (job.OutputPath is null || !System.IO.File.Exists(job.OutputPath))
            return NotFound();

        var contentType = job.Format == "json" ? "application/json" : "text/csv";
        var fileName = $"{job.ResourceType}-export-{job.JobId}.{job.Format}";
        return PhysicalFile(job.OutputPath, contentType, fileName);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken ct)
    {
        var job = await jobService.GetJobAsync(id, ct);
        if (job is null) return NotFound();

        var username = currentUser.GetCurrentUsername();
        if (job.RequestedBy != username)
        {
            var perms = await permissionService.GetEffectivePermissionsAsync(username, ct);
            if (!perms.Permissions.Contains(SystemPermissions.ManageUsers))
                return Forbid();
        }

        await jobService.SoftDeleteJobAsync(id, ct);
        return NoContent();
    }

    private static ExportJobDto ToDto(SyncExportJob j) => new(
        j.JobId, j.ParentJobId, j.RequestedBy, j.ResourceType, j.Format,
        j.Status, j.ProgressPercent, j.RowCount, j.ErrorMessage,
        j.ExpiresAt, j.CreatedAt, j.StartedAt, j.CompletedAt);
}

public sealed record CreateExportJobRequest(
    string ResourceType,
    string Format,
    string FiltersJson,
    Guid?  ParentJobId = null
);

public sealed record ExportJobDto(
    Guid            JobId,
    Guid?           ParentJobId,
    string          RequestedBy,
    string          ResourceType,
    string          Format,
    string          Status,
    int             ProgressPercent,
    long?           RowCount,
    string?         ErrorMessage,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
```

- [ ] **Step 14: Register everything in `Program.cs`**

In `src/MSOSync.App/Program.cs`, after the existing service registrations, add:

```csharp
// Export jobs
builder.Services.Configure<ExportOptions>(builder.Configuration.GetSection("Export"));
builder.Services.AddScoped<IExportJobService, ExportJobService>();
builder.Services.AddHostedService<ExportJobWorker>();
builder.Services.AddHostedService<ExportCleanupWorker>();
```

Add to `appsettings.json` (or `appsettings.Development.json`):
```json
"Export": {
  "ImmediateThreshold": 50000,
  "BasePath": "exports",
  "RetentionHours": 24,
  "MaxConcurrentJobs": 1
}
```

- [ ] **Step 15: Build both projects — expect zero warnings**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.App -c Debug --warnaserror 2>&1 | Select-Object -Last 5
dotnet build src/MSOSync.Api -c Debug --warnaserror 2>&1 | Select-Object -Last 5
```

Expected: `Build succeeded. 0 Warning(s)` for both.

- [ ] **Step 16: Write integration tests**

Look in `tests/MSOSync.IntegrationTests/` for the existing `IntegrationTestFactory` / `WebApplicationFactory` base class. Follow the exact same pattern as `PermissionsIntegrationTests.cs`.

```csharp
// tests/MSOSync.IntegrationTests/Export/ExportJobIntegrationTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MSOSync.Api.Controllers;
using Xunit;

namespace MSOSync.IntegrationTests.Export;

public sealed class ExportJobIntegrationTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task CreateJob_AsViewer_Returns202WithJobId()
    {
        var client = factory.CreateClientWithRole("VIEWER");
        var request = new CreateExportJobRequest("events", "csv", "{}", null);
        var resp = await client.PostAsJsonAsync("/api/v1/export-jobs", request);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("jobId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetJobs_ReturnsOnlyCallerJobs()
    {
        var viewer1 = factory.CreateClientWithRole("VIEWER");
        var viewer2 = factory.CreateClientWithRole("VIEWER");

        await viewer1.PostAsJsonAsync("/api/v1/export-jobs",
            new CreateExportJobRequest("events", "csv", "{}", null));

        var jobs = await viewer2.GetFromJsonAsync<ExportJobDto[]>("/api/v1/export-jobs");
        // viewer2 should see only their own jobs (zero here, since viewer2 created none)
        jobs.Should().NotBeNull();
    }

    [Fact]
    public async Task GetJobsAllTrue_AsViewer_Returns403()
    {
        var viewer = factory.CreateClientWithRole("VIEWER");
        var resp = await viewer.GetAsync("/api/v1/export-jobs?all=true");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DownloadJob_AsOtherViewer_Returns403()
    {
        var owner = factory.CreateClientWithRole("VIEWER");
        var other = factory.CreateClientWithRole("VIEWER");

        var createResp = await owner.PostAsJsonAsync("/api/v1/export-jobs",
            new CreateExportJobRequest("events", "csv", "{}", null));
        var body = await createResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var jobId = body.GetProperty("jobId").GetString()!;

        var resp = await other.GetAsync($"/api/v1/export-jobs/{jobId}/download");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteJob_AsOwner_Returns204()
    {
        var client = factory.CreateClientWithRole("VIEWER");
        var createResp = await client.PostAsJsonAsync("/api/v1/export-jobs",
            new CreateExportJobRequest("events", "csv", "{}", null));
        var body = await createResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var jobId = body.GetProperty("jobId").GetString()!;

        var resp = await client.DeleteAsync($"/api/v1/export-jobs/{jobId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify soft-deleted: download returns 404
        var download = await client.GetAsync($"/api/v1/export-jobs/{jobId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 17: Run integration tests**

```pwsh
dotnet test tests/MSOSync.IntegrationTests `
  --filter "FullyQualifiedName~ExportJobIntegrationTests" -c Debug `
  2>&1 | Select-Object -Last 10
```

Expected: 5 tests pass, 0 failed.

- [ ] **Step 18: Commit**

```pwsh
git add `
  src/MSOSync.Persistence/Entities/SyncExportJob.cs `
  src/MSOSync.Persistence/Configurations/SyncExportJobConfiguration.cs `
  src/MSOSync.Persistence/AppDbContext.cs `
  src/MSOSync.App/Export/ExportOptions.cs `
  src/MSOSync.App/Export/ExportJobChangedNotification.cs `
  src/MSOSync.App/Export/IExportJobService.cs `
  src/MSOSync.App/Export/ExportJobService.cs `
  src/MSOSync.App/Workers/ExportJobWorker.cs `
  src/MSOSync.App/Workers/ExportCleanupWorker.cs `
  src/MSOSync.App/SignalR/ExportJobChangedPublisher.cs `
  src/MSOSync.App/Program.cs `
  src/MSOSync.Api/Controllers/ExportJobController.cs `
  tests/MSOSync.IntegrationTests/Export/ExportJobIntegrationTests.cs

git add src/MSOSync.Persistence/Migrations/

git commit -m "feat(11g): add sync_export_job + ExportJobService + ExportJobWorker + ExportJobController + SignalR publisher"
```

## Status Report Format

```
Status: DONE
Commit: <sha>
Tests: <N> passed, 0 failed
Concerns: <none or list>
```
