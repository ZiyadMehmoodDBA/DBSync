namespace MSOSync.Common.Health;

public interface ISystemHealthContributor
{
    string Name { get; }
    Task<HealthContribution> GetAsync(CancellationToken ct);
}

public sealed record HealthContribution(
    string Name,
    string Level,      // "Healthy" | "Degraded" | "Unhealthy"
    string Summary,
    string? Detail = null);
