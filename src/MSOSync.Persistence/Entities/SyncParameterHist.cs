using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[HybridEntity]
public sealed class SyncParameterHist : IHybridEntity
{
    public long HistId { get; set; }
    public string ParameterName { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime? ChangeTime { get; set; }
    public Guid? TenantId { get; set; }  // null = system parameter history; non-null = tenant custom
}
