using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.Common.Workers;

namespace MSOSync.App.Health;

public sealed class WorkerHealthCheck(IWorkerStatusRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var workers = registry.GetAll();

        if (workers.Length == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No workers registered"));

        if (workers.Any(w => w.HealthState == WorkerHealthState.Failed))
            return Task.FromResult(HealthCheckResult.Unhealthy("One or more workers failed"));

        if (workers.Any(w => w.HealthState is WorkerHealthState.Warning or WorkerHealthState.Delayed))
            return Task.FromResult(HealthCheckResult.Degraded("One or more workers degraded"));

        return Task.FromResult(HealthCheckResult.Healthy($"{workers.Length} workers healthy"));
    }
}
