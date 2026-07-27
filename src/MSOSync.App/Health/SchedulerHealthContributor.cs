using MSOSync.Common.Health;
using MSOSync.Scheduler;

namespace MSOSync.App.Health;

/// <summary>
/// Contributes scheduler lock state to the /api/v1/system/health aggregator.
/// An instance where all jobs are Standby is healthy — peer instance is the active scheduler.
/// </summary>
public sealed class SchedulerHealthContributor(ISchedulerHealthReporter reporter)
    : ISystemHealthContributor
{
    public string Name => "Scheduler";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        var statuses     = reporter.GetAll();
        var standbyJobs  = statuses.Where(s => s.Mode == SchedulerJobMode.Standby).ToArray();
        var runningCount = statuses.Count(s => s.Mode == SchedulerJobMode.Running);

        string summary;
        if (statuses.Length == 0)
        {
            summary = "No scheduler jobs registered yet";
        }
        else if (standbyJobs.Length == statuses.Length)
        {
            summary = "This instance is scheduler standby — all jobs running on peer";
        }
        else
        {
            summary = $"{runningCount} job(s) active on this instance";
        }

        var detail = statuses.Length > 0
            ? string.Join("; ", statuses.Select(s =>
                $"{s.JobName}={s.Mode}" +
                (s.LockOwner is not null ? $"[{s.LockOwner}]" : string.Empty)))
            : null;

        return Task.FromResult(new HealthContribution(Name, "Healthy", summary, detail));
    }
}
