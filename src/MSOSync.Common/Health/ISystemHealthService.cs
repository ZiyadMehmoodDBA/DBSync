namespace MSOSync.Common.Health;

public interface ISystemHealthService
{
    Task<HealthContribution[]> GetAllAsync(CancellationToken ct);
}
