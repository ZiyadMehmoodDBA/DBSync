namespace MSOSync.Metadata.Configuration;

public interface IRolloutService
{
    Task<RolloutDto> StartRolloutAsync(StartRolloutRequest request, Guid userId, CancellationToken ct);
    Task<RolloutDto> GetRolloutAsync(Guid rolloutId, CancellationToken ct);
}
