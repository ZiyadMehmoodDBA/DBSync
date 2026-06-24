using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Scheduler.Workers;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());
        services.AddHostedService<SchedulerRecovery>();
        services.AddHostedService<SyncJob>();
        services.AddHostedService<RetryJob>();
        services.AddHostedService<PurgeJob>();
        services.AddHostedService<PullJob>();
        services.AddHostedService<HeartbeatWorker>();
        services.AddHostedService<ProbeWorker>();
        services.AddHostedService<NodeStatusWorker>();
        return services;
    }
}
