using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Dashboard;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Locks;
using MSOSync.Metadata.Metrics;
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

        // Epic 9C — Metrics APIs
        services.AddScoped<IMetricsQueryService, MetricsQueryService>();

        // Epic 9D — Audit & Administration APIs
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<ILockAdminService, LockAdminService>();
        services.AddScoped<IValidator<AuditFilter>, AuditFilterValidator>();

        // Epic 9E — Dashboard Query Optimization
        services.AddScoped<IDashboardQueryService, DashboardQueryService>();
        services.AddScoped<IValidator<ActivityFilter>, ActivityFilterValidator>();

        // Epic 11D — Export streaming
        services.AddScoped<IExportService<Events.EventFilter>,                  EventExportService>();
        services.AddScoped<IExportService<IncomingBatches.IncomingBatchFilter>, IncomingBatchExportService>();
        services.AddScoped<IExportService<Export.OutgoingBatchExportFilter>,    OutgoingBatchExportService>();
        services.AddScoped<IExportService<Audit.AuditFilter>,                   AuditExportService>();
        services.AddScoped<IExportAuditService, ExportAuditService>();

        return services;
    }
}
