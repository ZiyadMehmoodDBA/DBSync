using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());
        services.AddHostedService<SchedulerRecovery>();  // runs first
        services.AddHostedService<SyncJob>();
        services.AddHostedService<RetryJob>();
        services.AddHostedService<PurgeJob>();
        return services;
    }
}
