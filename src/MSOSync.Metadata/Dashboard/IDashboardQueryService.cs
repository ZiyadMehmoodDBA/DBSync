namespace MSOSync.Metadata.Dashboard;

public interface IDashboardQueryService
{
    Task<DashboardSummaryDto>            GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<ActivityItemDto>> GetActivityAsync(ActivityFilter filter, CancellationToken ct);
}
