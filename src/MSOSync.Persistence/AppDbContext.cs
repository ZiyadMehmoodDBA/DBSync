using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenantAccessor? _tenantAccessor;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentTenantAccessor?        tenantAccessor = null)
        : base(options)
    {
        _tenantAccessor = tenantAccessor;
    }

    public DbSet<SyncNode> Nodes => Set<SyncNode>();
    public DbSet<SyncNodeLifecycleHistory> NodeLifecycleHistories => Set<SyncNodeLifecycleHistory>();
    public DbSet<SyncNodeConnectivityHistory> NodeConnectivityHistories => Set<SyncNodeConnectivityHistory>();
    public DbSet<SyncNodeBootstrapToken> NodeBootstrapTokens => Set<SyncNodeBootstrapToken>();
    public DbSet<SyncConfigurationTemplate> ConfigurationTemplates => Set<SyncConfigurationTemplate>();
    public DbSet<SyncConfigurationTemplateVersion> ConfigurationTemplateVersions => Set<SyncConfigurationTemplateVersion>();
    public DbSet<SyncNodeConfigurationOverride> NodeConfigurationOverrides => Set<SyncNodeConfigurationOverride>();
    public DbSet<SyncNodeConfigurationHistory> NodeConfigurationHistories => Set<SyncNodeConfigurationHistory>();
    public DbSet<SyncConfigurationRollout> ConfigurationRollouts => Set<SyncConfigurationRollout>();
    public DbSet<SyncNodeGroup> NodeGroups => Set<SyncNodeGroup>();
    public DbSet<SyncNodeSecurity> NodeSecurities => Set<SyncNodeSecurity>();
    public DbSet<SyncRegistrationRequest> RegistrationRequests => Set<SyncRegistrationRequest>();
    public DbSet<SyncChannel> Channels => Set<SyncChannel>();
    public DbSet<SyncTrigger> Triggers => Set<SyncTrigger>();
    public DbSet<SyncTriggerHist> TriggerHists => Set<SyncTriggerHist>();
    public DbSet<SyncRouter> Routers => Set<SyncRouter>();
    public DbSet<SyncTriggerRouter> TriggerRouters => Set<SyncTriggerRouter>();
    public DbSet<SyncDataEvent> DataEvents => Set<SyncDataEvent>();
    public DbSet<SyncDataEventBatch> DataEventBatches => Set<SyncDataEventBatch>();
    public DbSet<SyncOutgoingBatch> OutgoingBatches => Set<SyncOutgoingBatch>();
    public DbSet<SyncIncomingBatch> IncomingBatches => Set<SyncIncomingBatch>();
    public DbSet<SyncBatchError> BatchErrors => Set<SyncBatchError>();
    public DbSet<SyncMonitor> Monitors => Set<SyncMonitor>();
    public DbSet<SyncRuntimeStats> RuntimeStats => Set<SyncRuntimeStats>();
    public DbSet<SyncAudit> Audits => Set<SyncAudit>();
    public DbSet<SyncParameter> Parameters => Set<SyncParameter>();
    public DbSet<SyncParameterHist> ParameterHists => Set<SyncParameterHist>();
    public DbSet<SyncLock> Locks => Set<SyncLock>();
    public DbSet<SyncUser> Users => Set<SyncUser>();
    public DbSet<SyncRole> Roles => Set<SyncRole>();
    public DbSet<SyncUserRole> UserRoles => Set<SyncUserRole>();
    public DbSet<SyncUserRefreshToken> UserRefreshTokens => Set<SyncUserRefreshToken>();
    public DbSet<SyncUserPreference> UserPreferences  => Set<SyncUserPreference>();
    public DbSet<SyncPermission>     Permissions      => Set<SyncPermission>();
    public DbSet<SyncRolePermission> RolePermissions  => Set<SyncRolePermission>();
    public DbSet<SyncExportJob>      ExportJobs       => Set<SyncExportJob>();
    public DbSet<SyncOperation>      Operations       => Set<SyncOperation>();
    public DbSet<SyncOperationStep>  OperationSteps   => Set<SyncOperationStep>();
    public DbSet<SyncReplayRequest>  ReplayRequests   => Set<SyncReplayRequest>();
    public DbSet<SyncReplayItem>     ReplayItems      => Set<SyncReplayItem>();
    internal DbSet<OidcConfiguration> OidcConfigurations => Set<OidcConfiguration>();
    public DbSet<SyncNodeScope>             NodeScopes             => Set<SyncNodeScope>();
    public DbSet<SyncNodeChannelAssignment> NodeChannelAssignments => Set<SyncNodeChannelAssignment>();
    public DbSet<SyncNodeTriggerAssignment> NodeTriggerAssignments => Set<SyncNodeTriggerAssignment>();
    public DbSet<SyncNodeRouterAssignment>  NodeRouterAssignments  => Set<SyncNodeRouterAssignment>();
    public DbSet<SyncNotification>     Notifications     => Set<SyncNotification>();
    public DbSet<SyncUserNotification> UserNotifications => Set<SyncUserNotification>();
    public DbSet<SyncPlugin>           Plugins           => Set<SyncPlugin>();
    public DbSet<SyncMarketplaceCache> MarketplaceCache  => Set<SyncMarketplaceCache>();
    public DbSet<Tenant>               Tenants           => Set<Tenant>();
    public DbSet<TenantMembership>     TenantMemberships => Set<TenantMembership>();

    // Referenced by tenant query filters. Must be an instance member: EF rewrites
    // context references in filters per instance, so the cached model never pins
    // a specific accessor (2A-015). Null = platform context — filter passes all rows.
    public Guid? CurrentTenantId => _tenantAccessor?.TenantId;

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PopulateTenantIds();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        PopulateTenantIds();
        return base.SaveChanges();
    }

    private void PopulateTenantIds()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantScoped>()
            .Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty))
        {
            // Use current tenant if available, fall back to SystemTenant for background/platform context
            entry.Entity.TenantId = _tenantAccessor?.TenantId ?? WellKnownTenantIds.SystemTenant;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Always applied so the cached model is identical for every context instance.
        // Without an accessor (unit tests, background workers) CurrentTenantId is null
        // and the filter passes all rows.
        modelBuilder.ApplyTenantFilters(this);
    }

    /// <summary>
    /// Creates a DbContextOptions builder for AppDbContext with warning suppression configured.
    /// Use this in test fixtures and design-time contexts where OnConfiguring is not invoked.
    /// </summary>
    public static DbContextOptionsBuilder<AppDbContext> CreateOptionsBuilder(string connectionString)
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}
