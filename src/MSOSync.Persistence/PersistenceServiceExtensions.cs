using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence.Lock;
using MSOSync.Persistence.Queries;

namespace MSOSync.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", schema)));

        services.AddScoped<GetPendingBatchesQuery>();
        services.AddScoped<GetOfflineNodesQuery>();
        services.AddScoped<GetRetryCandidatesQuery>();
        services.AddScoped<GetEventQueueDepthQuery>();
        services.AddScoped<GetNodeByIdQuery>();
        services.AddScoped<GetNodeSecurityQuery>();
        services.AddScoped<GetUserByUsernameQuery>();

        services.AddScoped<IDatabaseLockProvider, DatabaseLockProvider>();

        services.AddHealthChecks()
            .AddCheck<PersistenceHealthCheck>("database");

        return services;
    }
}
