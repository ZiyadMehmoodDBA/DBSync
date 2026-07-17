using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncAudit : ITenantScoped
{
    public long AuditId { get; set; }
    public string? Username { get; set; }
    public string? ActionName { get; set; }
    public string? ObjectName { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime? CreateTime { get; set; }
    public Guid TenantId { get; set; }
}
