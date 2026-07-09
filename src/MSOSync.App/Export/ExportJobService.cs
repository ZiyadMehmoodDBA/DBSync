using System.Diagnostics.Metrics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.Operations;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.App.Export;

public sealed class ExportJobService(
    AppDbContext db,
    IMediator mediator,
    IAuditService auditSvc,
    IOptions<ExportOptions> opts,
    IOperationService operationService)
    : IExportJobService
{
    private static readonly Meter               s_meter     = new("MSOSync.Export");
    private static readonly Counter<long>       s_created   = s_meter.CreateCounter<long>("msosync_export_jobs_created_total");
    private static readonly Counter<long>       s_completed = s_meter.CreateCounter<long>("msosync_export_jobs_completed_total");
    private static readonly Counter<long>       s_failed    = s_meter.CreateCounter<long>("msosync_export_jobs_failed_total");
    private static readonly Counter<long>       s_rows      = s_meter.CreateCounter<long>("msosync_export_rows_written_total");
    private static readonly Histogram<double>   s_duration  = s_meter.CreateHistogram<double>("msosync_export_job_duration_seconds");

    // Tracks JobId → OperationId for this service scope (one request / one background task).
    private readonly Dictionary<Guid, Guid> _operationIds = new();

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
        await auditSvc.WriteAsync("EXPORT_JOB_CREATED",
            $"Job {job.JobId} ({job.ResourceType}/{job.Format})",
            job.RequestedBy, ct);

        var operationId = await operationService.CreateAsync(
            type:          OperationType.Export,
            referenceId:   job.JobId,
            initiatedBy:   null,
            source:        OperationSource.User,
            correlationId: job.JobId.ToString(),
            canCancel:     true,
            canRetry:      false,
            summary:       $"Export {job.ResourceType} to {job.Format}",
            metadataJson:  $"{{\"format\":\"{job.Format}\",\"resourceType\":\"{job.ResourceType}\"}}",
            ct:            ct);

        _operationIds[job.JobId] = operationId;

        return job;
    }

    public Task<SyncExportJob?> GetJobAsync(Guid jobId, CancellationToken ct)
        => db.ExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == jobId, ct);

    public async Task<IReadOnlyList<SyncExportJob>> GetJobsForUserAsync(string username, CancellationToken ct)
    {
        var list = await db.ExportJobs.AsNoTracking()
            .Where(j => j.RequestedBy == username)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);
        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<SyncExportJob>> GetAllJobsAsync(CancellationToken ct)
    {
        var list = await db.ExportJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);
        return list.AsReadOnly();
    }

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

        var opId = await FindOperationIdAsync(jobId, ct);
        if (opId.HasValue)
            await operationService.UpdateProgressAsync(opId.Value, progressPercent, null, ct);
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
        await auditSvc.WriteAsync("EXPORT_JOB_COMPLETED",
            $"Job {jobId} ({job.ResourceType}/{job.Format}) rows={rowCount} duration={duration:F1}s",
            job.RequestedBy, ct);
        await PublishAsync(jobId, ct);

        var opId = await FindOperationIdAsync(jobId, ct);
        if (opId.HasValue)
            await operationService.CompleteAsync(opId.Value, OperationResult.Success,
                $"Exported {rowCount} rows to {Path.GetFileName(outputPath)}", ct);
    }

    public async Task FailJobAsync(Guid jobId, string errorMessage, CancellationToken ct)
    {
        await db.ExportJobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status,       ExportJobStatus.Failed)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedAt,  DateTimeOffset.UtcNow)
                .SetProperty(j => j.ExpiresAt,    DateTimeOffset.UtcNow.AddHours(opts.Value.RetentionHours)), ct);
        s_failed.Add(1);
        await PublishAsync(jobId, ct);

        var opId = await FindOperationIdAsync(jobId, ct);
        if (opId.HasValue)
            await operationService.CompleteAsync(opId.Value, OperationResult.Failure, errorMessage, ct);
    }

    public async Task SoftDeleteJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.ExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (!string.IsNullOrEmpty(job?.OutputPath) && File.Exists(job.OutputPath))
        {
            try { File.Delete(job.OutputPath); }
            catch (IOException) { /* file locked or already gone — proceed with DB update */ }
            catch (UnauthorizedAccessException) { /* no permission — proceed with DB update */ }
        }

        await db.ExportJobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ExportJobStatus.Deleted), ct);
        await PublishAsync(jobId, ct);
    }

    public async Task ExpireJobsAsync(CancellationToken ct)
    {
        await db.ExportJobs
            .Where(j => j.Status == ExportJobStatus.Completed || j.Status == ExportJobStatus.Failed)
            .Where(j => j.ExpiresAt != null && j.ExpiresAt < DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ExportJobStatus.Expired), ct);
    }

    private async Task<Guid?> FindOperationIdAsync(Guid jobId, CancellationToken ct)
    {
        if (_operationIds.TryGetValue(jobId, out var cached)) return cached;
        var op = await db.Operations.AsNoTracking()
            .Where(o => o.ReferenceId == jobId && o.OperationType == "Export")
            .OrderByDescending(o => o.StartedAt)
            .FirstOrDefaultAsync(ct);
        return op?.OperationId;
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
