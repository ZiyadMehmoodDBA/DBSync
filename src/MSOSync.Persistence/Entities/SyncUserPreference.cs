using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[HybridEntity]
public sealed class SyncUserPreference : IHybridEntity
{
    public long     PreferenceId    { get; set; }
    public long     UserId          { get; set; }
    public string   PreferenceKey   { get; set; } = "";
    public string   PreferenceValue { get; set; } = "";
    public DateTime UpdatedAt       { get; set; }
    public Guid? TenantId { get; set; }  // null = system preference; non-null = tenant-scoped preference

    public SyncUser User { get; set; } = null!;
}
