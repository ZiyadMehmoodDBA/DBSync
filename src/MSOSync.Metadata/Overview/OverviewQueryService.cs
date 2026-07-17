using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MSOSync.Common.Workers;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Metadata.Overview;

public sealed class OverviewQueryService(
    AppDbContext db,
    IPlatformRepository<SyncAudit> auditRepo,
    IWorkerStatusRegistry workerRegistry,
    OverviewSnapshotCache cache,
    IHostEnvironment env) : IOverviewQueryService
{
    public Task<OverviewDto> GetAsync(CancellationToken ct)
        => cache.GetOrCreateAsync(BuildSnapshotAsync, ct);

    private async Task<OverviewDto> BuildSnapshotAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var todayUtc = now.Date;

        // --- Node counts (by LifecycleState enum) ---
        var allNodes = await db.Nodes
            .AsNoTracking()
            .Select(n => new { n.LifecycleState, n.MaintenanceMode, n.ConfigurationState })
            .ToListAsync(ct);

        var totalNodes = allNodes.Count;
        var activeNodes = allNodes.Count(n => n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode);
        var maintenanceNodes = allNodes.Count(n => n.MaintenanceMode);
        var offlineNodes = allNodes.Count(n =>
            n.LifecycleState is NodeLifecycleState.Decommissioned
                             or NodeLifecycleState.Rejected
                             or NodeLifecycleState.Disabled);
        var degradedNodes = allNodes.Count(n =>
            n.LifecycleState is NodeLifecycleState.Recovery
                             or NodeLifecycleState.Decommissioning);

        // --- Pending registration requests ---
        var pendingRegistrations = await db.RegistrationRequests
            .AsNoTracking()
            .CountAsync(r => r.Status == RegistrationStatus.Pending, ct);

        // --- Configuration state ---
        var driftedCount = allNodes.Count(n => n.ConfigurationState == ConfigurationState.Drifted);
        var updateAvailableCount = allNodes.Count(n => n.ConfigurationState == ConfigurationState.UpdateAvailable);
        var configFailedCount = allNodes.Count(n => n.ConfigurationState == ConfigurationState.Failed);

        // --- Operations today ---
        var opCounts = await db.Operations
            .AsNoTracking()
            .Where(o => o.StartedAt >= todayUtc)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var runningOps = opCounts.FirstOrDefault(x => x.Status == "Running")?.Count ?? 0;
        var succeededOps = opCounts.FirstOrDefault(x => x.Status == "Completed")?.Count ?? 0;
        var failedOps = opCounts.FirstOrDefault(x => x.Status == "Failed")?.Count ?? 0;
        var queuedOps = opCounts.FirstOrDefault(x => x.Status == "Pending")?.Count ?? 0;

        // --- Recent audit events (top 10) ---
        var recentAuditEvents = await auditRepo.QueryAll()
            .Where(a => a.CreateTime != null)
            .OrderByDescending(a => a.CreateTime)
            .Take(10)
            .ToListAsync(ct);

        var recentActivity = recentAuditEvents.Select(a => new OverviewEventDto(
            EventId: a.AuditId.ToString(),
            OccurredAt: a.CreateTime!.Value,
            Category: DeriveCategory(a.ActionName ?? ""),
            Summary: a.ActionName ?? "Event",
            NodeId: a.ObjectName,
            CorrelationId: a.CorrelationId,
            DeepLink: DeriveEventDeepLink(a.ActionName, a.ObjectName)
        )).ToArray();

        // --- Worker health ---
        var workers = workerRegistry.GetAll();
        var workerHealthLevel = DeriveWorkerHealth(workers);

        // --- Node health level ---
        var nodeHealthLevel = offlineNodes > 0
            ? (offlineNodes > totalNodes * 0.1 ? "Unhealthy" : "Degraded")
            : "Healthy";

        // --- Cluster health (worst-of) ---
        var clusterHealth = DeriveClusterHealth(
            workerHealthLevel, nodeHealthLevel, workers, offlineNodes, totalNodes);

        // --- Warnings ---
        var warnings = new List<OverviewWarningDto>();
        if (offlineNodes > 0)
            warnings.Add(new OverviewWarningDto(
                Type: "NodeOffline",
                Severity: offlineNodes > totalNodes * 0.1 ? "Critical" : "Warning",
                Title: $"{offlineNodes} node(s) offline",
                Description: $"{offlineNodes} of {totalNodes} registered nodes are currently offline.",
                TargetRoute: "/operations/nodes",
                CorrelationId: null));

        if (driftedCount > 0)
            warnings.Add(new OverviewWarningDto(
                Type: "ConfigDrift",
                Severity: "Warning",
                Title: $"{driftedCount} node(s) with configuration drift",
                Description: "These nodes are running a configuration that differs from their assigned template.",
                TargetRoute: "/configuration",
                CorrelationId: null));

        var failedWorkers = workers.Where(w => w.HealthState == WorkerHealthState.Failed).ToArray();
        foreach (var fw in failedWorkers)
            warnings.Add(new OverviewWarningDto(
                Type: "WorkerFailed",
                Severity: "Critical",
                Title: $"Worker '{fw.WorkerName}' has failed",
                Description: fw.LastError ?? "No error detail available.",
                TargetRoute: "/admin/system",
                CorrelationId: null));

        // --- Process uptime ---
        var processUptime = now - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var uptimeStr = $"{(int)processUptime.TotalDays}d {processUptime.Hours:D2}:{processUptime.Minutes:D2}:{processUptime.Seconds:D2}";

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "12C";

        return new OverviewDto(
            Health: new OverviewHealthWidget(clusterHealth, workerHealthLevel, nodeHealthLevel),
            Operations: new OverviewOperationsWidget(runningOps, succeededOps, failedOps, queuedOps),
            Nodes: new OverviewNodesWidget(totalNodes, activeNodes, offlineNodes, maintenanceNodes, degradedNodes, pendingRegistrations),
            Configuration: new OverviewConfigurationWidget(driftedCount, updateAvailableCount, configFailedCount),
            Warnings: warnings.ToArray(),
            RecentActivity: recentActivity,
            System: new OverviewSystemWidget(
                Version: version,
                DatabaseMigration: "M026",
                Environment: env.EnvironmentName,
                Uptime: uptimeStr,
                SignalRStatus: "Configured",
                LastRefreshedAt: now),
            LastRefreshedAt: now);
    }

    private static string DeriveWorkerHealth(WorkerStatusDto[] workers)
    {
        if (workers.Any(w => w.HealthState == WorkerHealthState.Failed)) return "Unhealthy";
        if (workers.Any(w => w.HealthState is WorkerHealthState.Warning or WorkerHealthState.Delayed)) return "Degraded";
        return "Healthy";
    }

    private static string DeriveClusterHealth(
        string workerHealth, string nodeHealth,
        WorkerStatusDto[] workers, int offlineNodes, int totalNodes)
    {
        if (workerHealth == "Unhealthy") return "Critical";
        if (totalNodes > 0 && offlineNodes > totalNodes * 0.1) return "Critical";
        if (workerHealth == "Degraded" || nodeHealth == "Degraded") return "Degraded";
        if (offlineNodes > 0) return "Degraded";
        return "Healthy";
    }

    private static string DeriveCategory(string actionName) => actionName switch
    {
        var a when a.StartsWith("NODE_REGISTR") || a.StartsWith("NODE_APPROVED") || a.StartsWith("NODE_REJECTED") => "Registration",
        var a when a.StartsWith("NODE_") || a.StartsWith("BOOTSTRAP_") => "Lifecycle",
        var a when a.StartsWith("CONFIGURATION_") || a.StartsWith("ROLLOUT_") || a.StartsWith("HEARTBEAT_") => "Configuration",
        var a when a.StartsWith("EXPORT_") => "Operation",
        var a when a.StartsWith("AUTH_") || a.StartsWith("TOKEN_") => "Security",
        _ => "System"
    };

    private static string? DeriveEventDeepLink(string? actionName, string? entityId) =>
        actionName switch
        {
            var a when a is not null && a.StartsWith("EXPORT_") => "/operations/jobs",
            _ => null
        };
}
