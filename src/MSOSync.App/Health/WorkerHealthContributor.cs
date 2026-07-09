using MSOSync.Common.Health;
using MSOSync.Common.Workers;

namespace MSOSync.App.Health;

public sealed class WorkerHealthContributor(IWorkerStatusRegistry registry)
    : ISystemHealthContributor
{
    public string Name => "Workers";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        var workers = registry.GetAll();
        var total = workers.Length;

        if (total == 0)
            return Task.FromResult(new HealthContribution(Name, "Healthy", "No workers registered", null));

        var failedCount = workers.Count(w => w.HealthState == WorkerHealthState.Failed);
        var degradedCount = workers.Count(w =>
            w.HealthState is WorkerHealthState.Warning or WorkerHealthState.Delayed);
        var healthyCount = total - failedCount - degradedCount;

        if (failedCount > 0)
            return Task.FromResult(new HealthContribution(
                Name, "Unhealthy",
                $"{failedCount}/{total} worker(s) failed",
                string.Join("; ", workers
                    .Where(w => w.HealthState == WorkerHealthState.Failed)
                    .Select(w => $"{w.WorkerName}: {w.LastError}"))));

        if (degradedCount > 0)
            return Task.FromResult(new HealthContribution(
                Name, "Degraded",
                $"{degradedCount}/{total} worker(s) degraded, {healthyCount}/{total} healthy",
                null));

        return Task.FromResult(new HealthContribution(
            Name, "Healthy",
            $"{total}/{total} workers healthy",
            null));
    }
}
