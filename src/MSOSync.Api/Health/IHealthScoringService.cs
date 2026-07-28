namespace MSOSync.Api.Health;

public interface IHealthScoringService
{
    Task<IReadOnlyList<NodeHealthScore>> GetScoresAsync(CancellationToken ct = default);
    Task<NodeHealthScore?> GetScoreAsync(string nodeId, CancellationToken ct = default);
}
