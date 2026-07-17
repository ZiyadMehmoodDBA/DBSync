using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncDataEventBatch : ITenantScoped
{
    public long EventId { get; set; }
    public long BatchId { get; set; }
    public Guid TenantId { get; set; }
}
