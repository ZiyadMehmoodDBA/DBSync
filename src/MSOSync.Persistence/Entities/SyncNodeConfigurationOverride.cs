using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncNodeConfigurationOverride : ITenantScoped
{
    public Guid Id { get; set; }
    public string NodeId { get; set; } = null!;
    public string SettingKey { get; set; } = null!;
    public string SettingValue { get; set; } = null!;
    public string OverrideSource { get; set; } = "Manual";      // Manual / Imported / API
    public Guid UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid TenantId { get; set; }
}
