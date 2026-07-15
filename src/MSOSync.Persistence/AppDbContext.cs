using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

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
    public DbSet<SyncNodeScope>             NodeScopes             => Set<SyncNodeScope>();
    public DbSet<SyncNodeChannelAssignment> NodeChannelAssignments => Set<SyncNodeChannelAssignment>();
    public DbSet<SyncNodeTriggerAssignment> NodeTriggerAssignments => Set<SyncNodeTriggerAssignment>();
    public DbSet<SyncNodeRouterAssignment>  NodeRouterAssignments  => Set<SyncNodeRouterAssignment>();
    public DbSet<SyncNotification>     Notifications     => Set<SyncNotification>();
    public DbSet<SyncUserNotification> UserNotifications => Set<SyncUserNotification>();
    public DbSet<SyncPlugin>           Plugins           => Set<SyncPlugin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
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
