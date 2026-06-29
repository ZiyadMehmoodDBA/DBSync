using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Nodes;
using MSOSync.Metadata.Services;
using MSOSync.Metadata.Topology;
using MSOSync.Metadata.Users;

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

        // Existing services
        services.AddScoped<IParameterMetadataService, ParameterMetadataService>();
        services.AddScoped<INodeMetadataService, NodeMetadataService>();
        services.AddScoped<ITriggerMetadataService, TriggerMetadataService>();
        services.AddScoped<IRouterMetadataService, RouterMetadataService>();
        services.AddScoped<IChannelMetadataService, ChannelMetadataService>();
        services.AddScoped<INodeStateMachine, NodeStateMachine>();
        services.AddScoped<IUsersManagementService, UsersManagementService>();

        // Epic 9A — Operational Read APIs
        services.AddSingleton<IErrorSeverityClassifier, ErrorSeverityClassifier>();
        services.AddScoped<IEventQueryService, EventQueryService>();
        services.AddScoped<IIncomingBatchQueryService, IncomingBatchQueryService>();
        services.AddScoped<IBatchErrorQueryService, BatchErrorQueryService>();
        services.AddScoped<IValidator<EventFilter>, EventFilterValidator>();
        services.AddScoped<IValidator<IncomingBatchFilter>, IncomingBatchFilterValidator>();
        services.AddScoped<IValidator<BatchErrorFilter>, BatchErrorFilterValidator>();

        // Epic 9B — Topology APIs
        services.AddScoped<ITopologyQueryService, TopologyQueryService>();

        return services;
    }
}
