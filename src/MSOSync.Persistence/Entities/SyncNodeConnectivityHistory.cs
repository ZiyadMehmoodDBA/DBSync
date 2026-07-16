using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncNodeConnectivityHistory
{
    public long Id { get; set; }
    public string NodeId { get; set; } = null!;
    public ConnectivityStatus PreviousStatus { get; set; }
    public ConnectivityStatus NewStatus { get; set; }
    public ConnectivityReason Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
