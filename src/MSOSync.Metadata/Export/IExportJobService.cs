using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Export;

public interface IExportJobService
{
    Task<SyncExportJob> CreateJobAsync(
        string requestedBy, string resourceType, string format,
        string filtersJson, Guid? parentJobId, CancellationToken ct);

    Task<SyncExportJob?> GetJobAsync(Guid jobId, CancellationToken ct);

    Task<IReadOnlyList<SyncExportJob>> GetJobsForUserAsync(string username, CancellationToken ct);

    Task<IReadOnlyList<SyncExportJob>> GetAllJobsAsync(CancellationToken ct);

    /// <summary>Atomic claim — UPDATE...OUTPUT — safe for future multi-worker scenarios.</summary>
    Task<SyncExportJob?> ClaimNextPendingJobAsync(CancellationToken ct);

    Task UpdateProgressAsync(Guid jobId, int progressPercent, CancellationToken ct);

    Task CompleteJobAsync(Guid jobId, string outputPath, long rowCount, CancellationToken ct);

    Task FailJobAsync(Guid jobId, string errorMessage, CancellationToken ct);

    Task SoftDeleteJobAsync(Guid jobId, CancellationToken ct);

    Task ExpireJobsAsync(CancellationToken ct);
}
