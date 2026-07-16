using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncDataEventBatch
{
    public long EventId { get; set; }
    public long BatchId { get; set; }
}
