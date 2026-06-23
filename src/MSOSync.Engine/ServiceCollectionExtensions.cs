// src/MSOSync.Engine/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Engine;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplyEngine(this IServiceCollection services)
    {
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IApplyFailureClassifier, SqlApplyFailureClassifier>();
        services.AddSingleton<InsertBuilder>();
        services.AddSingleton<UpdateBuilder>();
        services.AddSingleton<DeleteBuilder>();
        services.AddScoped<ISqlEventApplicator, SqlEventApplicator>();
        services.AddScoped<ITriggerApplyMetadataService, TriggerApplyMetadataService>();
        services.AddScoped<IApplyService, ApplyEngine>();
        return services;
    }
}
