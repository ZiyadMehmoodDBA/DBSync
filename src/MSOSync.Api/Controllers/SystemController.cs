using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Dtos;
using MSOSync.Common.Health;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Overview;
using MSOSync.Scheduler;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class SystemController(
    ISystemHealthService     healthSvc,
    IWorkerStatusRegistry    workerRegistry,
    IOverviewQueryService    overviewSvc,
    ISchedulerHealthReporter schedulerHealth,
    IHostEnvironment         env) : ControllerBase
{
    [HttpGet("health")]
    [ProducesResponseType<HealthContribution[]>(200)]
    public async Task<IActionResult> GetHealthAsync(CancellationToken ct)
        => Ok(await healthSvc.GetAllAsync(ct));

    [HttpGet("workers")]
    [ProducesResponseType<WorkerStatusDto[]>(200)]
    public IActionResult GetWorkers()
        => Ok(workerRegistry.GetAll());

    [HttpGet("overview")]
    [ProducesResponseType<OverviewDto>(200)]
    public async Task<IActionResult> GetOverviewAsync(CancellationToken ct)
        => Ok(await overviewSvc.GetAsync(ct));

    [HttpGet("info")]
    [ProducesResponseType<SystemInfoDto>(200)]
    public IActionResult GetInfo()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var entryAssembly = Assembly.GetEntryAssembly();
        var version = entryAssembly?.GetName().Version?.ToString()
                      ?? entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? "12C";
        string? buildDate = null;
        var location = entryAssembly?.Location;
        if (!string.IsNullOrEmpty(location) && System.IO.File.Exists(location))
            buildDate = System.IO.File.GetLastWriteTimeUtc(location).ToString("O");
        return Ok(new SystemInfoDto(
            Version: version,
            BuildDate: buildDate,
            GitCommit: null,
            DotNetRuntime: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OperatingSystem: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            DatabaseMigration: "M025",
            Edition: "Community",
            Environment: env.EnvironmentName,
            ServerTime: DateTime.UtcNow.ToString("O"),
            ProcessUptime: $"{(int)uptime.TotalDays}d {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"));
    }

    [HttpGet("scheduler-status")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType<SchedulerStatusDto>(200)]
    public IActionResult GetSchedulerStatus()
    {
        var instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
        var jobs = schedulerHealth.GetAll()
            .Select(s => new SchedulerJobDto(
                s.JobName,
                s.Mode.ToString(),
                s.LockOwner,
                s.LockedSince,
                s.LastUpdated))
            .ToArray();

        return Ok(new SchedulerStatusDto(instanceId, jobs));
    }
}
