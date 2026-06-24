using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Nodes;
using MSOSync.Metadata.Services;

namespace MSOSync.Metadata;

public static class MetadataServiceExtensions
{
    public static IServiceCollection AddMetadata(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMemoryCache();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ParameterMetadataService>());
        services.AddScoped<IParameterMetadataService, ParameterMetadataService>();
        services.AddScoped<INodeMetadataService, NodeMetadataService>();
        services.AddScoped<ITriggerMetadataService, TriggerMetadataService>();
        services.AddScoped<IRouterMetadataService, RouterMetadataService>();
        services.AddScoped<IChannelMetadataService, ChannelMetadataService>();
        services.AddScoped<INodeStateMachine, NodeStateMachine>();
        return services;
    }
}
