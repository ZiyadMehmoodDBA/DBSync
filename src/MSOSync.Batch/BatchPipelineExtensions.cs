using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Batch;

public static class BatchPipelineExtensions
{
    public static IServiceCollection AddBatchPipeline(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddScoped<IBatchStateMachine, BatchStateMachine>();
        services.AddScoped<IBatchCreator, BatchCreator>();
        services.AddScoped<RetryProcessor>();
        services.AddScoped<BatchPurger>();
        services.AddScoped<IBatchTransportQueryService, BatchTransportQueryService>();
        return services;
    }
}
