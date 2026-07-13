namespace MSOSync.Metadata.Overview;

public interface IOverviewQueryService
{
    Task<OverviewDto> GetAsync(CancellationToken ct);
}
