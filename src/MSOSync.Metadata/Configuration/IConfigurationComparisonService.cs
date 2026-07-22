using MSOSync.Metadata.Configuration.Dtos;

namespace MSOSync.Metadata.Configuration;

public interface IConfigurationComparisonService
{
    Task<ConfigVersionDiffDto> CompareAsync(
        Guid templateId, int v1, int v2, CancellationToken ct = default);
}
