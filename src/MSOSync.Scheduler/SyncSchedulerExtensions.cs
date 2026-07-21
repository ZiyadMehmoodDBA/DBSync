using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Scheduler.Workers;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<HeartbeatOptions>(config.GetSection(HeartbeatOptions.Section));
        services.Configure<SyncOptions>(config.GetSection(SyncOptions.Section));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());
        services.AddHostedService<SchedulerRecovery>();
        services.AddHostedService<SyncJob>();
        services.AddHostedService<RetryJob>();
        services.AddHostedService<PurgeJob>();
        services.AddHostedService<PullJob>();
        services.AddHostedService<HeartbeatWorker>();
        services.AddHostedService<ProbeWorker>();
        // NodeStatusWorker deleted in Epic 12B-1 — lifecycle handled by NodeLifecycleState
        services.AddHostedService<ConnectivityEvaluator>();
        services.AddHostedService<DecommissionWorker>();
        services.AddHostedService<RollingOperationWorker>();
        return services;
    }
}
