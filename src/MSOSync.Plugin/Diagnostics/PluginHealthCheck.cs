using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Diagnostics;

public sealed class PluginHealthCheck(IPluginRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!registry.IsInitialized)
            return Task.FromResult(HealthCheckResult.Unhealthy("Plugin host not yet started"));

        var enabledPlugins = registry.GetAll()
            .Where(p => p.Status != PluginStatus.Disabled)
            .ToList();

        if (enabledPlugins.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No enabled plugins"));

        var failed = enabledPlugins
            .Where(p => p.Status == PluginStatus.Failed)
            .ToList();

        if (failed.Count > 0)
        {
            var details = string.Join(", ", failed.Select(f => $"{f.PluginId} ({f.ErrorMessage})"));
            return Task.FromResult(HealthCheckResult.Degraded($"Failed plugins: {details}"));
        }

        return Task.FromResult(
            HealthCheckResult.Healthy($"{enabledPlugins.Count} plugin(s) loaded"));
    }
}
