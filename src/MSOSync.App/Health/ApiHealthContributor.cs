using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MSOSync.Common.Health;

namespace MSOSync.App.Health;

public sealed class ApiHealthContributor : ISystemHealthContributor
{
    public string Name => "API";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var detail = $"Version: {version} | Runtime: {RuntimeInformation.FrameworkDescription} | " +
                     $"Uptime: {uptime:d\\d\\ hh\\:mm\\:ss}";
        return Task.FromResult(new HealthContribution(Name, "Healthy", "API is running", detail));
    }
}
