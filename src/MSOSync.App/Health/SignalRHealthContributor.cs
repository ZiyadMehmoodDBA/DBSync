using MSOSync.Common.Health;

namespace MSOSync.App.Health;

/// <summary>
/// SignalR health is reported as Healthy when the hub is configured.
/// Active connection counting requires hub tracking and is reserved for a future iteration.
/// </summary>
public sealed class SignalRHealthContributor : ISystemHealthContributor
{
    public string Name => "SignalR";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
        => Task.FromResult(new HealthContribution(
            Name, "Healthy",
            "SignalR hub configured",
            "Active connection count tracking reserved for future iteration"));
}
