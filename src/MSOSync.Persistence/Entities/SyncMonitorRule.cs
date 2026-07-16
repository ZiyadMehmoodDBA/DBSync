using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

// Tenant-scoped monitoring rule entity.
// DB table (MonitorRules) created in a future epic when the monitoring rules feature ships.
// Do NOT add a DbSet<SyncMonitorRule> to AppDbContext until that migration exists.
public sealed class SyncMonitorRule : ITenantScoped
{
    public Guid   RuleId      { get; set; }
    public Guid   TenantId    { get; set; }
    public string Name        { get; set; } = "";
    public string Expression  { get; set; } = "";
    public bool   IsEnabled   { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
