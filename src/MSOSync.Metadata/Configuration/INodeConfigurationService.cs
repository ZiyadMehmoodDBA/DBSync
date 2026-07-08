namespace MSOSync.Metadata.Configuration;

public interface INodeConfigurationService
{
    Task<CurrentConfigResult> GetCurrentAsync(string nodeId, string? ifNoneMatch, CancellationToken ct);
}
