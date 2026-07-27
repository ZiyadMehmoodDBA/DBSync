using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Scheduler.Internal;
using MSOSync.Scheduler.Workers;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Existing options
        services.Configure<HeartbeatOptions>(config.GetSection(HeartbeatOptions.Section));
        services.Configure<SyncOptions>(config.GetSection(SyncOptions.Section));

        // NEW in 2D.3: Distributed scheduler lock support
        services.AddOptions<SchedulerLockOptions>()
            .BindConfiguration(SchedulerLockOptions.Section)
            .Validate(
                o => o.TtlSeconds >= o.RenewalIntervalSeconds * 3,
                "Scheduler:Lock:TtlSeconds must be at least 3x RenewalIntervalSeconds")
            .ValidateOnStart();

        services.AddSingleton<ISchedulerHealthReporter, SchedulerHealthReporter>();
        services.AddSingleton<ISchedulerLockFactory, SchedulerLockFactory>();

        // Seed scheduler lock rows at startup
        services.AddHostedService<SchedulerLockSeeder>();

        // Existing MediatR + hosted services (unchanged)
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
        services.Configure<MSOSync.Metadata.Options.ReplayOptions>(
            config.GetSection(MSOSync.Metadata.Options.ReplayOptions.Section));
        services.AddHostedService<ReplayWorker>();

        return services;
    }
}
