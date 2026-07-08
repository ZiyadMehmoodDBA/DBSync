namespace MSOSync.Metadata.Configuration;

public interface IConfigurationAssignmentService
{
    Task<NodeConfigurationDto> AssignAsync(string nodeId, Guid templateId, int version,
        Guid userId, string? correlationId, CancellationToken ct);
    Task UnassignAsync(string nodeId, Guid userId, CancellationToken ct);
    Task<NodeConfigurationDto> GetNodeConfigurationAsync(string nodeId, CancellationToken ct);
    Task<IReadOnlyList<ConfigurationHistoryEventDto>> GetNodeHistoryAsync(string nodeId, CancellationToken ct);
    Task SetOverrideAsync(string nodeId, string key, string value, string source, Guid userId, CancellationToken ct);
    Task RemoveOverrideAsync(string nodeId, string key, Guid userId, CancellationToken ct);
    Task<DriftSummaryDto> GetDriftSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<DriftNodeDto>> GetDriftNodesAsync(string? stateFilter, Guid? templateId,
        int? version, string? nodeGroup, string? search, CancellationToken ct);
}
