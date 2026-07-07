using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// Fail-fast startup check (spec §3.4): every status value must parse to NodeLifecycleState;
/// soft inconsistencies are logged as errors but do not block startup.
public sealed class LifecycleStartupValidator(
    IServiceScopeFactory scopeFactory,
    ILogger<LifecycleStartupValidator> logger) : IHostedService
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Read raw status strings — do NOT materialize entities (enum conversion would
        // throw an opaque error; we want a precise diagnostic).
        // SqlQueryRaw is intentional here: schema name cannot be parameterized; value comes from env var only.
#pragma warning disable EF1002
        var statuses = await db.Database
            .SqlQueryRaw<string>($"SELECT status AS [Value] FROM [{Schema}].[sync_node]")
            .ToListAsync(ct);
#pragma warning restore EF1002

        var invalid = statuses
            .Where(s => !Enum.TryParse<NodeLifecycleState>(s, ignoreCase: false, out _))
            .Distinct()
            .ToList();

        if (invalid.Count > 0)
            throw new InvalidOperationException(
                $"Lifecycle startup validation failed: unparseable sync_node.status values: {string.Join(", ", invalid)}. " +
                "Run migration M022 or repair the data before starting.");

        var inconsistent = await db.Nodes.AsNoTracking()
            .Where(n => n.MaintenanceMode &&
                (n.LifecycleState == NodeLifecycleState.Decommissioned
                 || n.LifecycleState == NodeLifecycleState.Rejected))
            .Select(n => n.NodeId)
            .ToListAsync(ct);

        foreach (var nodeId in inconsistent)
            logger.LogError(
                "Lifecycle consistency: node {NodeId} is terminal but has MaintenanceMode=true", nodeId);

        logger.LogInformation("Lifecycle startup validation passed ({Count} nodes)", statuses.Count);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
