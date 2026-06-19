using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SyncNode> Nodes => Set<SyncNode>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
