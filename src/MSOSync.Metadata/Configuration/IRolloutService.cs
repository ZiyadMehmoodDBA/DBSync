namespace MSOSync.Metadata.Configuration;

public interface IRolloutService
{
    Task<RolloutDto> StartRolloutAsync(
        Guid templateId, int version, IReadOnlyList<string> nodeIds,
        Guid actorId, CancellationToken ct);
    Task<RolloutDto> GetRolloutAsync(Guid rolloutId, CancellationToken ct);
}
