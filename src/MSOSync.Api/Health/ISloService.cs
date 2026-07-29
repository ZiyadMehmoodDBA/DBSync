namespace MSOSync.Api.Health;

public interface ISloService
{
    Task<SloStatus> GetStatusAsync(CancellationToken ct = default);
}
