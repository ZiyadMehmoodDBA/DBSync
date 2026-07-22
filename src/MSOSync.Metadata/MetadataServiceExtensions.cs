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
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.Services;
using MSOSync.Metadata.Topology;
using MSOSync.Metadata.Permissions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Preferences;
using MSOSync.Metadata.Users;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Notifications;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Handlers;
using MSOSync.Metadata.Operations.Replay;
using MSOSync.Metadata.Operations.Rolling;
using MSOSync.Metadata.OutgoingBatches;
using MSOSync.Metadata.Overview;
using MSOSync.Metadata.Pagination;

namespace MSOSync.Metadata;

public static class MetadataServiceExtensions
{
    public static IServiceCollection AddMetadata(
        this IServiceCollection services,
        IConfiguration configuration)
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
        // Epic 12B-1 — Lifecycle policies
        services.AddSingleton<INodeSyncPolicy, NodeSyncPolicy>();
        services.AddSingleton<IConnectivityPolicy, ConnectivityPolicy>();
        services.AddHostedService<LifecycleStartupValidator>();

        // Epic 12B-1 — Lifecycle state machine + services
        services.Configure<LifecycleOptions>(configuration.GetSection(LifecycleOptions.Section));
        services.AddSingleton<INodeLifecycleStateMachine, NodeLifecycleStateMachine>();
        services.AddSingleton<NodeLifecycleLockRegistry>();
        services.AddScoped<IBootstrapTokenService, BootstrapTokenService>();
        services.AddScoped<INodeLifecycleHistoryService, NodeLifecycleHistoryService>();
        services.AddScoped<IDecommissionEvaluator, DecommissionEvaluator>();
        services.AddScoped<IUsersManagementService, UsersManagementService>();

        // Epic 9A — Operational Read APIs
        services.AddSingleton<IErrorSeverityClassifier, ErrorSeverityClassifier>();
        services.AddScoped<IEventQueryService, EventQueryService>();
        services.AddScoped<IIncomingBatchQueryService, IncomingBatchQueryService>();
        services.AddScoped<IOutgoingBatchQueryService, OutgoingBatchQueryService>();
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

        // Epic 11D — Audit summary
        services.AddScoped<IAuditSummaryService, AuditSummaryService>();

        // Epic 11E — User preferences
        services.AddScoped<IUserPreferencesService, UserPreferencesService>();

        // Epic 11F — Fine-grained RBAC
        services.AddScoped<IPermissionService, PermissionService>();

        // Epic 12A — Node Management
        services.AddScoped<IRegistrationDiffService, RegistrationDiffService>();
        services.AddScoped<INodeManagementService, NodeManagementService>();
        services.AddScoped<INodeLifecycleService, NodeLifecycleService>();
        services.AddScoped<INodeReadQueryService, NodeReadQueryService>();
        services.AddScoped<IProvisionPackageService, ProvisionPackageService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IValidator<NodeManagement.RegistrationFilter>, NodeManagement.RegistrationListFilterValidator>();

        // Epic 12B-1 — Lifecycle request validators + transition metadata provider
        services.AddSingleton<ITransitionMetadataProvider, TransitionMetadataProvider>();
        services.AddScoped<IValidator<Lifecycle.MaintenanceStartRequest>, Lifecycle.MaintenanceStartRequestValidator>();
        services.AddScoped<IValidator<Lifecycle.DecommissionRequest>, Lifecycle.DecommissionRequestValidator>();
        services.AddScoped<IValidator<Lifecycle.DisableRequest>, Lifecycle.DisableRequestValidator>();
        services.AddScoped<IValidator<Lifecycle.ActivateRequest>, Lifecycle.ActivateRequestValidator>();
        services.AddScoped<IValidator<Lifecycle.DrainRequest>, Lifecycle.DrainRequestValidator>();
        services.AddScoped<IValidator<Lifecycle.ResumeDrainRequest>, Lifecycle.ResumeDrainRequestValidator>();

        // Epic 12B-2 — Configuration Management
        services.AddScoped<IConfigurationValidationService, ConfigurationValidationService>();
        services.AddScoped<IConfigurationTemplateService, ConfigurationTemplateService>();
        services.AddScoped<IEffectiveConfigurationComputer, EffectiveConfigurationComputer>();
        services.AddScoped<IConfigurationAssignmentService, ConfigurationAssignmentService>();
        services.AddScoped<IRolloutService, RolloutService>();
        services.AddSingleton<IDriftDetector, DriftDetector>();
        services.AddScoped<INodeConfigurationService, NodeConfigurationService>();
        services.AddScoped<HeartbeatProcessor>();

        // Epic 12C — Operations registry
        services.AddScoped<IOperationService, OperationService>();
        services.AddScoped<IRollingOperationService, RollingOperationService>();
        services.AddScoped<IRollingOperationQueryService, RollingOperationQueryService>();
        services.AddKeyedScoped<IOperationHandler, ExportOperationHandler>(OperationType.Export);
        services.AddKeyedScoped<IOperationHandler, RolloutOperationHandler>(OperationType.Rollout);
        services.AddKeyedScoped<IOperationHandler, DecommissionOperationHandler>(OperationType.Decommission);
        services.AddScoped<IOperationQueryService, OperationQueryService>();

        // Epic 12C — Overview
        services.AddSingleton<OverviewSnapshotCache>();
        services.AddScoped<IOverviewQueryService, OverviewQueryService>();

        // Epic 12C.0 — Cursor HMAC signing
        services.AddSingleton<CursorSigner>();

        // Epic 12C — Correlation Timeline
        services.AddScoped<CorrelationTimelineAssembler>();

        // Epic 12C — Node Sync Scope
        services.AddScoped<INodeScopeService, NodeScopeService>();

        // Phase 2B.2 — Batch Replay
        services.Configure<MSOSync.Metadata.Options.ReplayOptions>(
            configuration.GetSection(MSOSync.Metadata.Options.ReplayOptions.Section));
        services.AddScoped<IReplayOperationService,      ReplayOperationService>();
        services.AddScoped<IReplayOperationQueryService, ReplayOperationQueryService>();

        // Phase 2B.3 — Advanced Operations Analytics
        services.AddScoped<IClusterSummaryQueryService, ClusterSummaryQueryService>();
        services.AddScoped<IConfigurationComparisonService, ConfigurationComparisonService>();

        // Epic 13 — Notifications
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationQueryService, NotificationQueryService>();

        // Epic 13 — Notification handlers are discovered via MediatR assembly scan above
        // (RegisterServicesFromAssemblyContaining<ParameterMetadataService> covers all handlers in this assembly)

        return services;
    }
}
