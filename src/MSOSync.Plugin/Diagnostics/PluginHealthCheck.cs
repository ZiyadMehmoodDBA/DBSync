using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Diagnostics;

public sealed class PluginHealthCheck(IPluginRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!registry.IsInitialized)
            return Task.FromResult(HealthCheckResult.Unhealthy("Plugin host not yet initialized"));

        var all     = registry.GetAll();
        var failed  = all.Count(p => p.Status == PluginStatus.Failed);
        var loaded  = all.Count(p => p.Status == PluginStatus.Loaded);
        var total   = all.Count;

        var data = new Dictionary<string, object>
        {
            ["total"]    = total,
            ["loaded"]   = loaded,
            ["failed"]   = failed,
            ["disabled"] = all.Count(p => p.Status == PluginStatus.Disabled),
        };

        if (failed > 0)
            return Task.FromResult(HealthCheckResult.Degraded(
                $"{failed} plugin(s) failed to load", data: data));

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{loaded}/{total} plugin(s) loaded", data: data));
    }
}
